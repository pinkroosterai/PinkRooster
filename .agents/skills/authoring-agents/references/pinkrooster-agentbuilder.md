# PinkRooster AgentBuilder reference

This reference describes the repository design verified on 2026-09-25; the Briefs section was added and verified against the source on 2026-10-03. Source code is authoritative; re-read it before editing.

## Relevant files

- `src/PinkRooster.Agents/AgentBuilder.cs`
- `src/PinkRooster.Agents/Prompts/PromptSections.cs` (the sections and the prompt layout the builder holds)
- `src/PinkRooster.Agents/Briefs/AgentBrief.cs` (the sections as a value)
- `src/PinkRooster.Agents/Prompts/AgentDefaults.cs`
- `src/PinkRooster.Agents/Extensions/AgentBuilderChatClientExtensions.cs`
- `tests/PinkRooster.Agents.Tests/Agents/AgentBuilderTests.cs`
- `tests/PinkRooster.Agents.Tests/Agents/AgentBriefTests.cs` and `AgentBuilderBriefTests.cs`
- `tests/PinkRooster.Agents.Tests/Agents/AgentDefaultsTests.cs`

## Construction

Start with:

```csharp
AgentBuilder builder = chatClient.CreateAgent();
```

The builder is mutable. Each fluent call changes and returns that same builder.

`Build()` may be called multiple times. Each call creates fresh chat/agent option and pipeline objects, while referenced tools, collections, providers, callbacks, and the chat client remain shared instances.

## Prompt composition

Unless `WithSystemPrompt` is used, Role is required.

The composed prompt sections are:

1. Role
2. Objective
3. Background
4. Instructions
5. Constraints
6. Output Format
7. Examples

Only non-empty sections are emitted.

### Instructions and constraints order

For each of Instructions and Constraints:

1. effective `AgentDefaults`;
2. ToolCollection text in the order collections were added;
3. builder-owned entries.

Exact duplicate lines are sent once using ordinal string comparison.

Do not depend on semantically equivalent wording being deduplicated.

## AgentDefaults

A builder uses its own `WithDefaults(...)` value when set, otherwise `AgentDefaults.BuiltIn`.

`WithoutDefaults()` is equivalent to `WithDefaults(AgentDefaults.None)`.

There is no process-wide mutable default. To change the defaults for many agents, build one `AgentDefaults` and pass it with `WithDefaults` from one shared method.

A built agent keeps the defaults read during its build.

The current built-in defaults cover general tool-use reliability, missing information, completion, not inventing tool results, not guessing missing tool arguments, and treating tool-returned text as data rather than instructions. Read the source before adding overlapping prompt rules.

## Raw system prompt gotcha

`WithSystemPrompt` replaces section composition.

The builder throws if a raw system prompt and any section are both used, whether the section came from a section method or from a brief.

A raw prompt does not include:

- `AgentDefaults`;
- ToolCollection standing Instructions;
- ToolCollection standing Constraints.

ToolCollections still contribute their tools through `BuildTools()` and, on the normal `Build()` path, their live context through `ToolCollectionChatClient`.

Therefore a raw prompt plus ToolCollections can retain capabilities while silently dropping their standing usage/policy guidance.

## Briefs: prompt sections as a value

`new AgentBrief(role: ..., constraints: [...])` holds the prompt sections as one value, and `WithBrief(brief)` applies them as the
section methods would. Use it to share lines such as house constraints between builders. A brief is made in code only.

- The constructor has the section methods' rules: blank text throws an `ArgumentException` naming the parameter, and `systemPrompt`
  with any section in one brief throws. A raw prompt from one call and a section from another fail at `Build()`, with the applied briefs' sources in the message.
- Applied in call order: a single-value section set again replaces the earlier one (the last call wins), and instructions,
  constraints and examples add up with the usual de-duplication. A brief with only constraints is valid; `Build()` still needs a role from some call, unless a raw prompt is used.
- A brief never holds `AgentDefaults`, tool-collection text, tools, approvals or metadata. Defaults and collection text still
  come first in Instructions and Constraints, so do not repeat them in the brief.

## Metadata

`WithId`, `WithName`, and `WithDescription` populate `ChatClientAgentOptions` metadata.

They do not become Role/Objective/Instructions.

MAF uses agent metadata for identification/observability, and an agent's name/description can be relevant when it is exposed as a function/MCP tool.

## Tool sources

The builder combines:

- tools supplied by `WithTool` / `WithTools(IEnumerable<AITool>)`;
- every `AIFunction` from ToolCollections supplied with `WithTools(ToolCollection...)`.

Supported direct helper forms include:

- `WithTool(AITool)`;
- `WithTool(Delegate)`, requiring PinkRooster's `[Tool]` attribute and honoring its name/description/approval;
- `WithTool(Delegate, name, description)`;
- `WithTools(IEnumerable<AITool>)`;
- `WithTools(params IEnumerable<ToolCollection>)`: one collection, several, or a list of them.

Tool names are checked for uniqueness ignoring case before the normal ChatOptions callbacks run.

### ConfigureChatOptions can bypass tool validation

`CreateOptions()` builds and validates the builder's tools first, then invokes `ConfigureChatOptions` callbacks.

A callback that replaces or mutates `ChatOptions.Tools` can bypass the builder's duplicate-name/source validation.

Avoid changing `Tools` there unless that bypass is explicitly intended and separately tested.

## Approval

`RequireApproval(names)` is resolved at build time against the tools the builder knows about, ignoring case.

If a named tool does not exist, build fails with a message listing known tools.

If the tool is already `ApprovalRequiredAIFunction` it is not double-wrapped.

If the named tool is an `AIFunction` it is wrapped.

If the named tool is a non-function `AITool`, build fails because this approval mechanism only wraps functions.

Answering is separate from marking. `RunWithApprovalsAsync` and `RunStreamingWithApprovalsAsync` take a callback that returns `bool`, or one that returns `ApprovalAnswer` and so can refuse with a reason the model reads. `PermissionPolicy.AnswerAsync` is the second shape: it allows `Read` and `State` tools, decides `Edit` and `Execute` by its mode (`Ask`, `AcceptEdits`, `Plan`) and its allow rules, never lets a mode allow a tool without a kind, and asks the user through the function it was created with. A prefix rule (`AllowRule.ForPrefix(tool, argument, prefix)`) never matches text that chains commands. The policy knows a tool's kind only for the tools given to `WithTools`.

## Background tools

`AllowBackground(names)` is resolved at build time against the tools the builder knows about, ignoring case, like `RequireApproval`. `[AgentAllowBackground(names)]` is its attribute form for agent classes.

Build fails, naming the fix, when a named tool does not exist, is a non-function `AITool`, or already has a parameter named `runInBackground`.

Each named tool is wrapped so its schema has one more optional boolean, `runInBackground`. Approval stays outermost: a tool that needs approval is approved first and then starts as a task.

Allowing any tool adds `BackgroundTasksToolCollection` (`WaitForTasks`, `GetTaskResult`, `ListTasks`, `CancelTask`) and gives the agent an inbox. `WaitForTasks` returns the result of every listed task that has ended, and takes `waitForAll`.

`WithInbox()` gives an inbox without background tools. An inbox switches on MAF's message injection with per-service-call history persistence for that agent; a `ConfigureAgentOptions` callback that switches either off fails the build.

`ConfigureBackground(options => ...)` sets `BackgroundOptions`: `MaxRunningTasks` (6), `TaskTimeLimit` (30 minutes), `GracePeriod` (30 seconds), `MaxResultCharacters` (20,000), `DefaultWaitTimeout` (300 seconds), `MaxWaitTimeout` (1 hour) and `MaxWakes` (10). It needs `AllowBackground` or `WithInbox`.

A task belongs to the run that started it. The run stays alive while a task runs; when it ends, running tasks are cancelled. A run that pauses for an approval keeps its tasks for the run that carries the answers. Task ids count on within a session. A second run on a session while one is in progress throws.

A wake is the model being called again because a task ended after it had ended its turn; a call for a host's message is not counted. At `MaxWakes` the run ends, its running tasks are cancelled, and `BackgroundWakeLimitReached` and a warning say so.

A failed task's exception is logged and is on `BackgroundTaskEnded`; the model reads its message only when the tool loop has `IncludeDetailedErrors`.

`agent.PostMessageAsync(session, message)` (namespace `PinkRooster.Agents`) puts a message in the inbox of a run in progress. It throws when no run is in progress on the session or the agent has no inbox. A host's message ends a waiting `WaitForTasks`; a task update does not.

With `ConfigureToolLoop`, a posted message or task update is read one model call later than without it, because MAF places its message injection above a tool loop the builder supplies. `Build()` logs a warning.

`BuildOptions()` refuses a builder with `AllowBackground`, `WithInbox` or `ConfigureBackground`: tasks and the inbox are kept by the agent `Build()` makes.

## Context and history

`WithChatHistoryProvider` sets the single `ChatHistoryProvider`.

`WithContextProvider` appends `AIContextProvider` instances; order is preserved in `ChatClientAgentOptions.AIContextProviders`.

ToolCollection context is a separate PinkRooster concern:

- normal `Build()` places `ToolCollectionChatClient` inside the chat-client pipeline so collections can refresh context on model calls, including calls in the function loop;
- `BuildOptions()` cannot build that client, so it appends a context-only `ToolCollectionContextProvider` instead, changing collection-context frequency to the agent-context path.

Do not assume these paths are semantically interchangeable for rapidly changing context.

## ConfigureChatOptions

Callbacks run after PinkRooster sets:

- `ChatOptions.Instructions`;
- `ChatOptions.Tools`.

Callbacks execute in registration order on a fresh `ChatOptions` object per build.

They may overwrite the builder's values.

Use this hook for options the fluent API does not cover. Avoid casually replacing Instructions/Tools.

## ConfigureAgentOptions

Callbacks run after `CreateOptions()` and after the client pipeline has been prepared, immediately before `ChatClientAgent` construction.

They execute in registration order and can overwrite already-populated agent options.

This is the broadest escape hatch. Prefer first-class builder methods whenever possible.

Repository rule: keep MAF's default client wrapping and never set `UseProvidedChatClientAsIs`.

## ConfigureClient and pipeline order

`Build()` begins from `client.AsBuilder()`.

When configured, the pipeline includes:

1. PinkRooster's configured function-invocation client when `ConfigureToolLoop` is used;
2. `ToolCollectionChatClient` when collections exist;
3. `EventingChatClient`;
4. middleware added through `ConfigureClient` in callback order;
5. the underlying provider client according to ChatClientBuilder semantics.

Read the current implementation before relying on exact inside/outside ordering. The builder documentation states middleware added first sits nearest collection context.

`BuildOptions()` refuses `ConfigureClient` because options alone cannot carry client middleware.

## ConfigureToolLoop

Without this hook, PinkRooster relies on MAF's normal `ChatClientAgent` function-invocation wrapping.

When one or more tool-loop callbacks are registered, `Build()` explicitly calls `UseFunctionInvocation` and applies callbacks to a fresh `FunctionInvokingChatClient`.

Typical customizations include maximum iterations or detailed error behavior.

`BuildOptions()` refuses this hook because it does not build the client.

## Events

`OnEvent` registers PinkRooster event handlers.

Handlers:

- run synchronously;
- run in registration order;
- can be invoked concurrently when parallel tool calls emit events;
- receive events from nested builder-made agents, with parent-run information;
- are always caught and logged when they throw (to nowhere without a logger), `OperationCanceledException` included, and never fail the run or change a tool's result.

`BuildOptions()` refuses event handlers because it returns no PinkRooster event-wrapped agent.

## Logger factory

`WithLoggerFactory` supplies logging to the MAF agent and PinkRooster layers that use it, including collection-context/event error paths.

It is configuration, not an event sink replacement.

## Apply

`Apply(Action<AgentBuilder>)` is useful for shared house configuration.

Use it for genuinely shared settings. Avoid hiding agent-specific responsibility or surprising tool additions in a generic helper that makes a builder hard to audit.

## Clone

`Clone()` copies the builder's lists and scalar settings.

It does **not** clone the referenced:

- chat client;
- tools;
- ToolCollections;
- context/history providers;
- callbacks/delegates;
- logger factory.

Use Clone to derive prompt/config variants only when shared component lifetimes are intentional.

## Build

`Build()`:

1. composes/validates the prompt and tools;
2. creates the chat-client pipeline;
3. applies agent options callbacks;
4. constructs a `ChatClientAgent`;
5. wraps tool-call events;
6. applies the middleware added with `Use` and the step loop of a `SteppedAgent`, in the order added (one step loop per builder; a second fails `Build()`);
7. returns the PinkRooster eventing wrapper.

`agent.GetService<ChatClientAgent>()` can retrieve the inner chat-client agent through the wrapper.

## BuildOptions

`BuildOptions()` returns only `ChatClientAgentOptions` for callers who will construct a hand-built agent.

It refuses:

- agent middleware (`Use`);
- `OnEvent`;
- `ConfigureClient`;
- `ConfigureToolLoop`.

With collections, it adds their context through `ToolCollectionContextProvider.ForContextOnly`.

Do not use `BuildOptions()` when exact `Build()` behavior is required.

## Testing implications

Useful AgentBuilder tests observe behavior through `RecordingChatClient` / `ScriptedChatClient` rather than only inspecting private builder state.

Depending on the change, cover:

- composed prompt and section order;
- defaults/collection text;
- raw-system-prompt bypass behavior;
- tool list and duplicate-name validation;
- approval wrapping;
- context/history integration;
- callback ordering;
- Clone isolation versus shared references;
- Build-twice independence;
- BuildOptions parity and its known differences;
- streaming and events when relevant.

Prompt/composition tests should be paired with behavioral cases when wording changes are intended to alter model behavior.
