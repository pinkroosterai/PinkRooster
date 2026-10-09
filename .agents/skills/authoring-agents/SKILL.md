---
name: authoring-agents
description: Create, modify, review, and improve PinkRooster agents composed with AgentBuilder, or written as agent classes (DeclaredAgent, SteppedAgent, the brief attributes, Configure, own [Tool] methods, [AgentTools], [AgentDescription], [AgentWithoutDefaults], [AgentToolLoop], [AgentRequireApproval], GetContextAsync, OnEvent). Use for Role/Objective/Background/Instructions/Constraints/Output Format/Examples, tools and approvals, context/history providers, metadata, chat/agent/client/tool-loop configuration, events, Build versus BuildOptions, choosing between a builder chain and an agent class, or choosing between a plain, stepped or sub-agent shape. Do not use this skill to implement domain/business logic or to change AgentBuilder's framework implementation unless explicitly requested.
---

# Authoring PinkRooster agents

Use this skill to design and compose agents with `PinkRooster.Agents.AgentBuilder`, and to write the same agents as classes that derive from `DeclaredAgent` or `SteppedAgent`.

The goal is the smallest agent definition that reliably expresses the agent's stable responsibility, capabilities, boundaries, context, and output contract. Do not solve tool, state, orchestration, or enforcement problems by piling more prose into the system prompt.

## Scope

This skill may:

- create a new agent definition or reusable builder factory;
- modify an existing builder chain while preserving its application semantics;
- review and improve prompt sections, tool composition, approvals, providers, options, middleware, events, metadata, or build path;
- choose between a plain agent, a stepped agent, an agent-as-tool and a workflow;
- add or update agent-composition tests and evaluation cases;
- compose existing ToolCollections, tools, context providers, history providers, middleware, and services.

This skill must not invent domain/business behavior. It must not implement a missing tool's business logic, a new persistence system, retrieval algorithm, authorization policy, or middleware behavior merely to make the agent compile.

When the task requires authoring or substantially redesigning a `ToolCollection`, use the repository's `authoring-tool-collections` skill for that part.

Changing the public behavior or internals of `AgentBuilder` itself is framework work, not normal agent authoring. Do that only when the user explicitly asks for a framework/API change.

## Read the repository first

Before editing an agent, read the current versions of:

- `src/PinkRooster.Agents/AgentBuilder.cs`
- `src/PinkRooster.Agents/Briefs/AgentBrief.cs` when builders share sections as a value;
- `src/PinkRooster.Agents/Prompts/AgentDefaults.cs`
- `src/PinkRooster.Agents/Extensions/AgentBuilderChatClientExtensions.cs`
- `src/PinkRooster.Agents/DeclaredAgent.cs` and `src/PinkRooster.Agents/Steps/SteppedAgent.cs` when the agent is a class;
- the target agent definition/factory and its tests;
- the ToolCollections, context/history providers and middleware it uses.


Repository source is authoritative when it differs from these references.

Read:

- [references/agent-design.md](references/agent-design.md) for prompt and capability-placement principles;
- [references/pinkrooster-agentbuilder.md](references/pinkrooster-agentbuilder.md) for builder semantics and gotchas;
- [references/microsoft-agent-framework.md](references/microsoft-agent-framework.md) for MAF composition;
- [references/agent-classes.md](references/agent-classes.md) when the agent is, or should become, a class;
- [references/review-rubric.md](references/review-rubric.md) for review work.

## Choose the mode

### Create

Before writing a builder chain, identify:

1. **Responsibility** — the narrow job this agent owns.
2. **Success** — what counts as done for one request.
3. **Stable context** — facts that belong in its standing prompt.
4. **Dynamic context** — facts that change per user/session/run.
5. **Capabilities** — tools or sub-agents it genuinely needs.
6. **State/history** — what must survive between turns.
7. **Side effects** — which actions need enforcement or approval.
8. **Output contract** — free text, a stable textual shape, or structured output.
9. **Execution shape** — plain agent, stepped agent, agent-as-tool, or workflow.

Choose the execution shape before polishing prompt wording.

### Modify

Read the full builder chain, all shared `Apply(...)` helpers, attached ToolCollections/providers, call sites, and tests first.

Preserve business behavior. Prefer the smallest change that fixes the agent contract. If a requested improvement actually requires a tool, provider, persistence, orchestration, or business-layer change, state that dependency rather than hiding it in prompt text.

### Review / improve

Begin read-only. Apply [references/review-rubric.md](references/review-rubric.md).

Prioritize problems that can cause:

- the wrong responsibility or execution pattern;
- wrong/missed tool calls;
- unsafe or unenforced actions;
- stale or overlarge context;
- history/session leakage;
- contradictory instructions;
- malformed or unnecessarily fragile output;
- callback/middleware ordering surprises;
- avoidable model calls or token cost.

If the user asked for improvements, apply contract/composition fixes that do not invent business behavior.

## Builder chain or agent class

Both build the same agent through `AgentBuilder`, so the prompt rules, tool rules, events and approvals are identical. Pick by where the agent lives:

- **Builder chain** (`chatClient.CreateAgent()…Build()`): the agent is defined where it is used, once, with no tools of its own and no steps of its own.
- **Agent class** (derive from `DeclaredAgent`): the agent is reused, has `[Tool]` methods or context of its own, or a host wants a type to pass around, derive from and test.
- **Stepped agent class** (derive from `SteppedAgent`): the agent takes several turns in one run and the steps are its own.

Do not move a working builder chain to a class without a reason from this list. [references/agent-classes.md](references/agent-classes.md) has the rules for the class forms, and `assets/agent-class-template.cs` the template.

## Prefer the simplest execution shape

Start with:

```csharp
chatClient.CreateAgent()
```

Move to a stepped agent, agent-as-tool, or workflow only when its additional calls/state/control solve a concrete requirement.

Do not add self-review, planning, chains, sub-agents, or middleware merely because the task is "complex."


## Compose the standing prompt by responsibility

Prefer the builder's sections over `WithSystemPrompt` for normal PinkRooster agents.

The sections are written with `WithRole` and the other section methods. To share lines such as house constraints between builders, put them in an
`AgentBrief` and give it to `WithBrief(brief)`. A brief holds sections only: tools, approvals, defaults and metadata stay in the builder chain. See
[references/pinkrooster-agentbuilder.md](references/pinkrooster-agentbuilder.md#briefs-prompt-sections-as-a-value).

### Role

`WithRole` answers: **who is this agent for this application?**

Keep it concise and domain-specific. Role is required unless a raw system prompt is used.

Good roles establish responsibility, not theatrical persona.

### Objective

`WithObjective` answers: **what outcome defines success?**

Describe the end state or definition of done. Do not turn it into a long procedure.

### Background

`WithBackground` is stable information the agent needs across requests to interpret its job correctly.

Do not put:

- changing account/session state;
- current search/database results;
- large documents;
- secrets;
- facts the model should retrieve only when relevant.

Those belong in user input, a context provider, or a tool.

### Instructions

Use `WithInstruction(s)` for non-obvious operational behavior that spans the agent.

Prefer explicit conditions and actions over vague aspirations.

Do not add generic filler such as "think carefully", "be intelligent", "always use best practices", or chain-of-thought instructions. Current reasoning models work best from a clear goal, constraints, and output contract without prescribing private reasoning.

Do not duplicate:

- behavior already in `AgentDefaults`;
- one tool's usage rules from its tool description;
- ToolCollection standing instructions;
- rules already enforced by code.

### Constraints

Use `WithConstraint(s)` for boundaries the model must observe.

A prompt constraint is guidance, not an enforcement boundary. Authorization, validation, tool approval, rate limits, data access, and other hard controls belong in code, approval mechanisms, or middleware.

### Output Format

Use `WithOutputFormat` only when the agent should consistently answer in one textual shape.

When the application needs machine-readable data, prefer MAF structured output (`RunAsync<T>` or a response format on compatible agents) rather than asking for "JSON only" as prose.

Do not impose a rigid format that conflicts with tool approvals, clarification turns, errors, or materially different request types.

### Examples

Try zero-shot first.

Use `WithExample` when evaluation shows a non-obvious pattern, edge case, or output contract is not learned reliably from instructions alone.

Examples are permanent system-prompt tokens. Keep only relevant, internally consistent examples that justify that cost. Do not use examples to smuggle dynamic data into the standing prompt.

## Treat WithSystemPrompt as an escape hatch

Use `WithSystemPrompt` when preserving a complete externally-owned/raw prompt is itself the requirement.

Do not combine it with section methods; the builder rejects that combination.

Important PinkRooster behavior: a raw system prompt bypasses `AgentDefaults` and the standing Instructions/Constraints supplied by ToolCollections. The collections' tools and live context can still participate, so using a raw prompt can silently remove guidance those tools were designed to receive.

Before using `WithSystemPrompt` with ToolCollections, inspect their standing text and decide deliberately whether the raw prompt must incorporate equivalent guidance.

## Put capabilities in the correct layer

Use this rule:

- **Standing prompt:** stable responsibility and behavior.
- **Tool / ToolCollection:** on-demand action or lookup chosen by the model.
- **AIContextProvider:** dynamic information that should be proactively supplied for a run, such as memory/RAG/session-derived context.
- **ChatHistoryProvider:** storage/retrieval policy for conversation history.
- **Chat-client middleware:** cross-cutting behavior around model calls such as logging, telemetry, caching, or request transformation.
- **Tool-loop configuration:** bounded function-invocation-loop behavior.
- **Agent middleware:** run-level orchestration or behavior.
- **Application code / approval:** hard enforcement and side-effect control.
- **User message/run options:** request-specific instructions and data.

If information is needed only occasionally, prefer a tool over injecting it on every run. If the model must always see it for the current run, a context provider is usually the better abstraction.

## Add tools deliberately

Attach only capabilities needed by the agent's responsibility.

Use `WithTools(ToolCollection...)` when the collection's standing text and live context are part of the capability. Use `WithTool` for an isolated existing function/tool where a collection is unnecessary.

Check tool names across every tool the builder can see. They must be unique ignoring case.

Use `RequireApproval("ToolName")` for an **agent-specific** approval policy. Prefer approval on the ToolCollection/tool itself when approval is intrinsic wherever that tool is used.

`RequireApproval` only works for function tools. Do not try to use it as a generic guard around arbitrary hosted tools.

When a host should not ask about every call, answer approvals with a `PermissionPolicy` (`PinkRooster.Agents.Permissions`) instead of a yes-or-no callback: `new PermissionPolicy(ask).WithTools(collections)`, then `agent.RunWithApprovalsAsync(prompt, session, policy.AnswerAsync)`. Its mode (`Ask`, `AcceptEdits`, `Plan`) decides by each tool's `ToolKind`, allow rules (`AllowRule.ForTool`, `ForPrefix`, `ForWholeText`) cover the rest, and a refusal tells the model why. The policy only sees calls that need approval, so every tool that edits or executes must still be marked. Give the same `policy.AnswerAsync` to a sub-agent collection's `ApproveToolCallsWith`, and tell the policy the tool sets' tools.

Use `AllowBackground("ToolName")` when a tool can take long and the agent has other work to do meanwhile, such as a sub-agent run or a build. The tool gets one more optional parameter, `runInBackground`; a call with it returns a task id at once, and the agent gets `WaitForTasks`, `GetTaskResult`, `ListTasks` and `CancelTask` to follow the task. A call without it runs as before. On an agent class, `[AgentAllowBackground("ToolName")]` does the same.

Before allowing a tool, check that it may run at the same time as other calls on the same collection instance. `AllowBackground` asserts that; the builder cannot check it. A collection that keeps one piece of state per instance, or rewrites a file without locking it, is not safe to allow without reading it. For the built-in collections, the BuiltIn guide has a table.

Set the limits with `ConfigureBackground` only when the defaults do not fit: tasks at once, the time one task may run, and how often a run may call the model again because a task ended. Do not repeat the limits or the task tools in the agent's instructions; the task collection already tells the model.

A run lasts until the model has answered and no task is running, so an agent with background tools can run much longer than its last model call. A host that shows progress should listen to `BackgroundTaskStarted`, `BackgroundTaskEnded` and `BackgroundWaitStarted`.

Use `WithInbox()` when the host must be able to hand the agent a message while it runs, with `agent.PostMessageAsync(session, message)` (or `TryPostMessageAsync`, which returns false when no run can read it), and the agent has no background tool; `AllowBackground` gives an inbox too. A terminal host that lets the user type during a run needs an inbox for `RunWithInputAsync` of `PinkRooster.SpectreConsole`. Do not combine an inbox with `ConfigureToolLoop` unless a message read one model call later is acceptable.

When tools need redesign, switch to the `authoring-tool-collections` skill rather than compensating with agent instructions.

## Choose dynamic context and history deliberately

Use `WithChatHistoryProvider` when the default in-session history policy is not sufficient.

Use `WithContextProvider` for MAF context providers such as compaction, memory, retrieval, TodoProvider, or AgentModeProvider.

Provider instances may serve multiple sessions. Per-conversation mutable state belongs in the session/provider state mechanism, not casually in provider instance fields.

To keep a conversation beyond the process, save the session after every run with `SessionStore` (`PinkRooster.Agents.Sessions`): `SaveAsync(agent, session, prompt)`, `List()`, `RestoreAsync(agent, id)`. Restore with an agent configured as the one that saved; the restore names tools that are gone or new. State kept in agent or provider instance fields is not saved, which is one more reason to keep it in the session.

Do not put dynamic per-user state into an `AgentBuilder` factory's static prompt.

Provider order can matter. Register providers intentionally and inspect their current implementation when one depends on another's output.

For ToolCollections specifically, remember that PinkRooster's normal `Build()` path injects collection context in the chat-client pipeline so it can be fresh on model calls inside the tool loop. Do not assume an arbitrary `WithContextProvider` has identical frequency semantics; verify the pinned MAF behavior when freshness matters.

## Use advanced configuration hooks sparingly

Prefer the fluent builder method for a setting whenever one exists.

### ConfigureChatOptions

Runs after the builder has created `ChatOptions` with composed instructions and tools.

Use it for chat settings the builder does not expose, such as an intentional response format or provider-neutral request option.

Avoid changing `Instructions` or `Tools` through this callback unless bypassing builder composition/validation is explicitly intended.

Callbacks run in registration order; later callbacks can overwrite earlier values.

### ConfigureAgentOptions

Runs after PinkRooster has populated `ChatClientAgentOptions` and is the last escape hatch before construction.

Use it only for MAF options the builder does not cover. Avoid overwriting `ChatOptions`, metadata, history, or context-provider fields that were already configured with first-class builder methods.

Repository rule: do not set `UseProvidedChatClientAsIs`. PinkRooster intentionally preserves MAF's default client wrapping.

### ConfigureClient

Use for chat-client middleware such as logging, OpenTelemetry, caching, or other provider-neutral cross-cutting model-call behavior.

Middleware order matters. Read the current builder pipeline before inserting behavior that depends on relative position.

This hook exists only in `Build()`; `BuildOptions()` cannot carry client middleware.

### ConfigureToolLoop

Use only when the default function loop needs an explicit bound or behavior change.

Once used, PinkRooster supplies its configured `FunctionInvokingChatClient` loop. Do not add it routinely.

Be cautious with detailed error exposure in production because tool exceptions may contain internal data.

## Metadata is not prompt behavior

`WithId`, `WithName`, and `WithDescription` populate agent metadata.

- Use a stable ID only when the surrounding application needs one.
- Use Name for a human-readable identity.
- Use Description to summarize the agent's purpose/capability.

Description is especially important when the agent is later exposed as a function/MCP tool, because agent metadata can become tool/server metadata. It does not replace Role or Instructions.

Do not put secrets or dynamic user data in metadata.

## Keep defaults unless there is a reason not to

By default the builder uses `AgentDefaults.BuiltIn`.

The shipped defaults contain baseline tool-use reliability and tool-result trust guidance. Do not call `WithoutDefaults()` merely to shorten the prompt.

Use `WithDefaults(...)` for an agent-specific replacement.

There is no process-wide default to change. To extend the shipped set for every agent, build one `AgentDefaults` from `AgentDefaults.BuiltIn` and pass it to `WithDefaults` from the method all agent factories call.

## Build versus BuildOptions

Prefer `Build()`.

Use `BuildOptions()` only when the caller deliberately constructs its own `ChatClientAgent`.

`BuildOptions()` cannot represent:

- `ConfigureClient`;
- `ConfigureToolLoop`;
- `OnEvent`;

It also handles ToolCollection context through an agent context provider rather than the normal builder's per-model-call collection-context client, so freshness frequency differs.

Do not switch from `Build()` to `BuildOptions()` as a stylistic refactor.

## Clone and shared configuration

`AgentBuilder` is mutable. Fluent calls change that instance.

Use `Clone()` when deriving related variants from a shared base definition. The builder's lists/settings are copied, but referenced tools, ToolCollections, providers, callbacks, and the client are shared objects.

Before cloning agents with mutable components, verify their intended lifetime and concurrency semantics.

`Build()` may be called more than once; each build gets fresh options/pipeline objects, while the referenced shared components remain the same instances.

## Observability

Use `OnEvent` when application/UI code needs PinkRooster's run, text, reasoning, step, model-call (with usage and timing) and tool-call events; `OnEvent<TEvent>` hears one event type. A brief can also be made in code with `new AgentBrief(role: ...)` and written with `ToJson()` / `Save(path)`.

Event handlers are synchronous and can be called concurrently when tool calls run in parallel. Keep them fast and thread-safe.

A throwing handler is always caught and logged (to nowhere without `WithLoggerFactory`) and the run goes on, `OperationCanceledException` included; a handler that must not fail silently logs for itself. A run publishes exactly one `RunCompleted` (outcomes: `Succeeded`, `Failed`, `Cancelled`, `AwaitingApproval`, `Abandoned` for a stream the consumer stopped reading), and each started tool call exactly one `ToolCallCompleted`.

Use MAF/client OpenTelemetry or logging middleware for cross-cutting production telemetry rather than trying to reconstruct all telemetry from prompt text or user-visible events.

## Validate behavior, not only composition

For code changes:

1. build the affected project;
2. run the relevant tests;
3. use `RecordingChatClient` / `ScriptedChatClient` or an equivalent test client to observe the actual prompt, tools, calls, and responses;
4. verify tool selection and approval behavior;
5. verify history/context behavior across sessions when applicable;
6. verify raw-system-prompt/default/collection interactions;
7. verify callback ordering when advanced hooks are used;
8. verify streaming if the application depends on it;
9. verify structured output with the target client/model when required.

Create representative evaluation cases for:

- requests that should use each tool;
- requests that should not use tools;
- ambiguous/insufficient-input cases;
- relevant edge cases and constraints;
- output-contract cases;
- multi-turn/session behavior when relevant.

Prefer local deterministic checks for tool calls, structure, and invariants. Use model-based evaluation only where semantic quality requires it.

Do not optimize prompts by intuition alone. Preserve a regression set when an observed model failure motivated a prompt change.

## Finish with an agent contract report

For create/modify work, report:

- execution shape chosen and why;
- prompt sections added/changed;
- tools and approvals;
- context/history providers;
- advanced options/middleware/events;
- output contract;
- validation/evaluation performed;
- unresolved dependencies or business-logic work.

For review-only work, report findings in priority order, with the concrete agent failure, safety issue, latency/cost, or maintenance problem each can cause.

Do not claim an agent capability exists when the underlying tool/provider/business behavior is still missing.
