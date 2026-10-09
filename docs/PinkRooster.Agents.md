# PinkRooster.Agents

A fluent agent builder for the [Microsoft Agent Framework](https://learn.microsoft.com/agent-framework/) (MAF),
on .NET 10. Compose an agent's system prompt from named sections in one chain, give it
[tool collections](https://github.com/pinkroosterai/PinkRooster/blob/main/docs/PinkRooster.ToolCollections.md), watch what it does through events, and keep
everything MAF offers within reach.

```text
dotnet add package PinkRooster.Agents --prerelease
```

Needs the .NET 10 SDK and an `IChatClient` for your model. It depends on `PinkRooster.ToolCollections`, the Microsoft Agent Framework and Microsoft.Extensions.AI. It has no model provider.
The result is a normal MAF `AIAgent`: run it with `RunAsync` / `RunStreamingAsync` and sessions, as any other.

Related packages: [`PinkRooster.ToolCollections`](https://github.com/pinkroosterai/PinkRooster/blob/main/docs/PinkRooster.ToolCollections.md) (the tool classes an agent uses),
[`PinkRooster.ToolCollections.BuiltIn`](https://github.com/pinkroosterai/PinkRooster/blob/main/docs/PinkRooster.ToolCollections.BuiltIn.md) (shell, files, ask-the-user, task list, date/time, C# compiler tools),
[`PinkRooster.ToolCollections.Mcp`](https://github.com/pinkroosterai/PinkRooster/blob/main/docs/PinkRooster.ToolCollections.Mcp.md) (tools from MCP servers).

**Why use it**

- You keep a normal MAF `AIAgent`: `RunAsync`, streaming, sessions and workflows work as usual.
- The system prompt comes from named sections in a fixed order, with defaults; a repeated line is sent once.
- The sections can be shared between agents as an `AgentBrief`, or written as attributes on an agent class.
- Mistakes fail at the call or at `Build()` with a message that names what to change.
- One typed event stream covers reasoning, answer text, tool calls and approvals, for `RunAsync` and `RunStreamingAsync`.

## Why not MAF directly, or Harness?

MAF gives you the agent runtime; this package gives you a precise way to say what the agent *is* (sections), what it can *do* (tool
collections) and what it is *doing* (events), and hands you back a MAF agent. It adds to MAF and replaces none of it: `Build()` returns an
`AIAgent` that runs in a MAF workflow, behind MAF's `ToolApprovalAgent` and anywhere else an `AIAgent` goes.

MAF's own `instructions` string is the whole system prompt. Here, the Role, Objective, Background, Instructions, Constraints, Output Format
and Examples sections are composed into that one string, in a fixed order, with defaults and your tool collections' text, and checked at
`Build()`. Role / Objective / Background plays the part that role / goal / backstory plays in CrewAI.

`HarnessAgent` (package `Microsoft.Agents.AI.Harness`) is MAF's batteries-included agent for long autonomous work: default instructions, a
to-do list, plan and execute modes, file memory, skills, compaction. This package is the small, explicit alternative: you choose the prompt,
the tools and the events, and nothing else is added. The two combine: [give a tool collection to a harness
agent](#agents-the-builder-did-not-make).

**Coming from Semantic Kernel?** A plugin class with `[KernelFunction]` methods becomes a `ToolCollection` with `[Tool]` methods. MAF's migration
guide registers functions one by one with `AIFunctionFactory.Create`; a collection keeps them in one class, together with the prompt lines they
need, a note on their current state, and which of them need approval.

**Supported MAF range.** `PinkRooster.Agents` depends on `Microsoft.Agents.AI` at `[1.24.0, 1.25.0)`. MAF has shipped a minor version every one to two weeks since June 2026, and the step loop sits on experimental MAF types, so each new MAF minor needs a release of every package here before you can move
to it. That is the price of never meeting a changed experimental signature at run time.

### Words used here

| Word | Means |
|---|---|
| **brief** | The prompt sections as data: built in code, or written as attributes on a class. |
| **section** | One part of the brief: Role, Objective, Background, Instructions, Constraints, Output Format or Examples. |
| **system prompt** | What is sent to the model: the sections composed in a fixed order, or the raw text of `WithSystemPrompt`. MAF calls it `instructions`. |
| **model call** | One request to the model. A tool-using turn holds several. |
| **turn** | One run of the agent inside a step loop. It can hold several model calls when tools are used. |
| **step** | A labelled phase of a stepped agent. A step can take several turns (a retry). |
| **final step** | A step after which the loop stops without asking what comes next. It does not say which reply is the answer: that is the last turn's reply. |
| **agent middleware** | MAF's word for an agent that wraps another; `AgentBuilder.Use(...)` adds one. |

**Try it:** the [samples](https://github.com/pinkroosterai/PinkRooster/blob/main/samples/README.md) show each feature in a console project of its own, from
[`Builder`](https://github.com/pinkroosterai/PinkRooster/blob/main/samples/PinkRooster.Samples.Builder/README.md) to
[`Approvals`](https://github.com/pinkroosterai/PinkRooster/blob/main/samples/PinkRooster.Samples.Approvals/README.md) and `SteppedAgents`.

## Quickstart

The example below uses the shell tool, so install the built-in tools as well:

```text
dotnet add package PinkRooster.Agents --prerelease
dotnet add package PinkRooster.ToolCollections.BuiltIn --prerelease
```

<!-- quickstart:begin -->
```csharp
using PinkRooster.Agents;
using PinkRooster.ToolCollections.BuiltIn.Shells;

var agent = chatClient
    .CreateAgent()
    .WithRole("You are a build assistant for this repository.")
    .WithTools(new ShellToolCollection())
    .Build();

var response = await agent.RunAsync("Does the solution build?");
Console.WriteLine(response.Text);
```
<!-- quickstart:end -->

The answer as a string is `response.Text`; `ToString()` on the response gives the same. To draw a markdown answer on a Spectre.Console terminal, `AnsiConsole.Console.WriteAnswer(response.Text)` from [`PinkRooster.SpectreConsole`](https://github.com/pinkroosterai/PinkRooster/blob/main/docs/PinkRooster.SpectreConsole.md) formats it.

`chatClient` is any `IChatClient`. For an OpenAI-compatible server, such as LM Studio, Ollama's `/v1`
or LocalAI, add `Microsoft.Extensions.AI.OpenAI` to your app (the libraries here stay
provider-neutral):

```csharp
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

IChatClient chatClient = new OpenAIClient(
        new ApiKeyCredential("not-needed-locally"),
        new OpenAIClientOptions { Endpoint = new Uri("http://localhost:1234/v1") })
    .GetChatClient("qwen2.5-7b-instruct")
    .AsIChatClient();
```

The repository's tests compile and run this quickstart and keep it within 8 lines of code. It uses the shell tool from
[`PinkRooster.ToolCollections.BuiltIn`](https://github.com/pinkroosterai/PinkRooster/blob/main/docs/PinkRooster.ToolCollections.BuiltIn.md).

## Composing an agent

```csharp
using Microsoft.Agents.AI;
using PinkRooster.Agents;
using PinkRooster.ToolCollections.BuiltIn;
using PinkRooster.ToolCollections.BuiltIn.Shells;

AIAgent agent = chatClient
    .CreateAgent()
    .WithRole("You review pull requests.")
    .WithObjective("Find bugs before they merge.")
    .WithInstruction("Quote the line you mean.")
    .WithConstraint("Never approve a change you have not read.")
    .WithOutputFormat("A numbered list, most severe first.")
    .WithTools(new ShellToolCollection(new Workspace(repoRoot)))
    .Build();
```

An MCP server's tools go in the same way, wrapped in a collection that carries their instructions
and approvals: see [`PinkRooster.ToolCollections.Mcp`](https://github.com/pinkroosterai/PinkRooster/blob/main/docs/PinkRooster.ToolCollections.Mcp.md).

The sections are always sent in this order: Role, Objective, Background, Instructions,
Constraints, Output Format, Examples. `brief.Examples` holds `AgentExample(Input, Output)` records.
- **Role** is required, unless `WithSystemPrompt` sets the whole prompt instead.
- **Instructions and Constraints** list, in order:
  - `AgentDefaults.BuiltIn` (a short built-in set);
  - each tool collection's own text;
  - the agent's own lines.

  A repeated line is sent once. A line with line breaks stays inside its bullet: the later lines are
  indented under it. `WithDefaults(...)` or `WithoutDefaults()` changes the first part
  for one agent.
- **Shared setup** goes in one method and is applied with `Apply(HouseRules)`.
- **`Clone()`** copies a builder.

Mistakes fail at the call or at `Build()`, with a message that names what to change. For
example: no role, a raw prompt combined with sections, two tools with one name, or approval asked
for a tool that does not exist.

### Sharing sections between builders

The section methods have a value form: an `AgentBrief` holds the same sections, and `WithBrief(brief)` applies them as if the section
methods had been called in the same order:

```csharp
using Microsoft.Agents.AI;
using PinkRooster.Agents;
using PinkRooster.Agents.Briefs;

AgentBrief houseRules = new(constraints: ["Never approve a change you have not read."], outputFormat: "A numbered list, most severe first.");

AIAgent reviewer = chatClient.CreateAgent().WithBrief(houseRules).WithRole("You review pull requests.").Build();
AIAgent triager = chatClient.CreateAgent().WithBrief(houseRules).WithRole("You triage issues.").Build();
```

- **Several sources** combine in the order of the calls. A single-value section (role, objective, background, output
  format, system prompt) set twice takes the last call; instructions, constraints and examples add up; a repeated line
  is sent once.
- **Rules** are the section methods': blank text throws an `ArgumentException` that names the parameter, and `systemPrompt` cannot be
  in the same brief as a section.
- **Defaults and tool collections** are not in a brief. `AgentDefaults` and each collection's text still come first.

### Everything else MAF offers

| Need | Builder call |
|---|---|
| Keep history somewhere other than memory | `WithChatHistoryProvider(provider)` |
| Compaction, memory, retrieval | `WithContextProvider(provider)` (any MAF `AIContextProvider`) |
| Logging, OpenTelemetry, caching on the client | `ConfigureClient(pipeline => pipeline.UseLogging())` |
| Tool-loop limits and error detail | `ConfigureToolLoop(loop => loop.MaximumIterationsPerRequest = 10)` |
| Temperature, response format, other request options | `ConfigureChatOptions(options => ...)` |
| Any other agent option | `ConfigureAgentOptions(options => ...)` |
| See what the agent is doing | `OnEvent(item => ...)` — see [Events](#events) |
| Put a loop, a reviewer or a router around the agent | `Use((agent, services) => ...)`, MAF's agent middleware — see [Your own middleware](#your-own-middleware) |
| Services for middleware and the client pipeline | `WithServices(provider)` |
| Build the agent yourself | `new ChatClientAgent(client, builder.BuildOptions())` |

`BuildOptions()` delivers collection context once per run instead of once per model call. It
throws if `ConfigureClient`, `ConfigureToolLoop`, `OnEvent` or `Use` was used, because it builds no client
and no agent.

## Agent classes

Define an agent as a class when it is reused, has tools of its own, or takes steps of its own. Keep the builder for an
agent defined where it is used. An agent class is built through the same builder, so the prompt, the tool rules, the
events and the approvals are the same.

<!-- agent-class:begin -->
```csharp
using Microsoft.Extensions.AI;
using PinkRooster.Agents;

[AgentRole("You review pull requests.")]
[AgentInstruction("Quote the line you mean.")]
public sealed class ReviewerAgent(IChatClient chatClient) : DeclaredAgent(chatClient);

var agent = new ReviewerAgent(chatClient);
var response = await agent.RunAsync("Review #12");
Console.WriteLine(response.Text);
```
<!-- agent-class:end -->

An instance is a MAF `AIAgent`: `RunAsync`, `RunAsync<T>`, `RunStreamingAsync`, sessions, approvals and `AsAIFunction()` work on
it. Its name is the class name unless `Configure` sets another. `agent.GetService<ChatClientAgent>()` builds the agent and
returns the inner one, so a mistake in the class surfaces there without a model call.

A class whose constructor takes only the client is also created with `chatClient.CreateAgent<ReviewerAgent>()`, which reads like the builder's `chatClient.CreateAgent()`; a class with more constructor parameters is created with `new`.

**The brief** is one attribute per section: `[AgentRole]`, `[AgentObjective]`, `[AgentBackground]`, `[AgentInstruction]`,
`[AgentConstraint]`, `[AgentOutputFormat]` and `[AgentExample]`. Role, objective, background and output format appear once per
class; instructions, constraints and examples may repeat. On a class hierarchy a single-text section on the derived class replaces
the base's, and the lists run base first. The text is constant: text that depends on a constructor argument goes in `Configure`.

**Settings.** Four more attributes each set one builder option, and `Configure` runs after them and wins:
`[AgentDescription("...")]` is the agent's description, which a model reads when the agent is given to it as a tool through `AsAIFunction()`;
`[AgentWithoutDefaults]` leaves `AgentDefaults` out of the prompt; `[AgentToolLoop(MaximumIterationsPerRequest = 10)]` sets the tool-loop
limits it names and nothing else (`MaximumConsecutiveErrorsPerRequest`, `IncludeDetailedErrors`, `AllowConcurrentInvocation`,
`TerminateOnUnknownCalls` too); `[AgentRequireApproval("RunShell")]` asks the host's approval for tools of the collections the class
names; and `[AgentAllowBackground("RunShell")]` lets the model start a tool as a [background task](#background-tools). On a class hierarchy a
derived class's description and tool-loop values replace the base's, and approval and background names add up. An
`[AgentToolLoop]` makes the builder supply the tool loop, so `AsAgentBuilder().BuildOptions()` refuses such a class, as it does after `ConfigureToolLoop`.

**Tools and context.** Public `[Tool]` methods on the class are its tools, named, described and approval-marked like a
collection's. `[AgentTools(typeof(ShellToolCollection), typeof(DateTimeToolCollection))]` names tool collections, each created
through its public parameterless constructor, one object per agent instance; on a class hierarchy the base's come first. A collection that must be connected first, such as an MCP server's, comes from an attribute derived from `ToolCollectionSourceAttribute`, for example `[AgentMcpHttpTools]`; the agent creates it on first use and `DisposeAsync` on the agent disposes it. A collection that needs
constructor arguments, context providers and everything else the builder offers go in `Configure`, which runs once
per instance, after the attributes, the class's own tools and the named collections, so what it sets wins a clash:

```csharp
using Microsoft.Extensions.AI;
using PinkRooster.Agents;
using PinkRooster.ToolCollections;
using PinkRooster.ToolCollections.BuiltIn;
using PinkRooster.ToolCollections.BuiltIn.Shells;

[AgentRole("You are a build assistant.")]
public sealed class BuildAgent(IChatClient chatClient, string repoRoot) : DeclaredAgent(chatClient)
{
    protected override void Configure(AgentBuilder agent) => agent
        .WithTools(new ShellToolCollection(new Workspace(repoRoot)))
        .RequireApproval("RunShell")
        .WithInstruction($"The repository is at {repoRoot}.");

    [Tool("Returns the name of the current git branch.")]
    public string CurrentBranch() => Git.Branch(repoRoot);

    protected override ValueTask<string?> GetContextAsync(CancellationToken cancellationToken) =>
        new($"## Repository\n{repoRoot}");
}
```

`GetContextAsync` is the class's own context, under the rules of a tool collection's: asked before every model call, sent in
the one marked message ahead of the collections' text, and never stored in the session's history.

**Events.** Override `OnEvent(AgentEvent)` to observe the class's own events. A host that did not write the class adds a
handler on the instance; each call returns an `IDisposable` that removes the handler at once. A handler hears the runs that
start after it was added, and every session's runs, which it tells apart by `RunId`:

```csharp
using PinkRooster.Agents.Eventing;

IDisposable text = reviewer.OnEvent<AssistantTextDelta>(delta => Console.Write(delta.Text));
text.Dispose();
```

**As a builder.** `AsAgentBuilder()` returns a new `AgentBuilder` that already holds what the class declares: the brief, its own
tools and context, and what `Configure` set. Add to it and build a separate agent, for example
`new ReviewerAgent(chatClient).AsAgentBuilder().WithTools(extra).Build()`. That agent is not the instance: handlers added with
`reviewer.OnEvent(...)` do not hear its runs, and the class's own `[Tool]` methods still run on the instance, so they share its state. A class
with a step loop already holds it, so adding a second step loop throws.

**One instance, many sessions.** What a run has to remember lives in the session, never in a field. A `[Tool]` method,
`GetContextAsync` or a handler that needs the session of the run in progress reads `AIAgent.CurrentRunContext.Session`. A run
started without a session gets one, so it is never null there.

**Steps.** An agent that takes several turns in one run derives from `SteppedAgent` and fills in two members: `MaxTurns` caps the
turns of one message, a tool approval in between included, and `NextAsync` runs after each turn and returns `NextStep.Stop()`, which
makes the last reply the answer, or `NextStep.Send(label, prompt)`. `StartAsync` is optional: override it to set the run's state with
`step.SetState` and to enter the first step with `step.Enter`; when it enters none, the loop enters a step labelled `start`.
`step.Turn` is the number of turns finished for this message: 0 in `StartAsync`, 1 in the first `NextAsync`.

```csharp
using Microsoft.Extensions.AI;
using PinkRooster.Agents;
using PinkRooster.Agents.Steps;

[AgentRole("You translate documents.")]
public sealed class TranslatorAgent(IChatClient chatClient) : SteppedAgent(chatClient)
{
    protected override int MaxTurns => 3;

    protected override ValueTask<NextStep> NextAsync(StepContext step, CancellationToken cancellationToken) =>
        ValueTask.FromResult(step.LastReply.Contains("translated")
            ? NextStep.Stop()
            : NextStep.Send("revise", "Rewrite it in plain words."));
}
```

Every step stays in the session's history. `RunAsync` returns the last turn's reply; a streamed run yields every turn, with the
step prompts left out. A run keeps its place and its state in the session (`SetState`, `GetState`), never in a field, so one instance
serves many sessions and a session can be saved and restored while a run waits for an approval. A tool approval pauses the run, and
the answers on the same session continue it at the paused step. A step entered with `isFinal: true` ends the run: `NextAsync` is not
called after it, and `NextStep.Send(label, prompt, isFinal: true)` sets it for the step it enters. Final says nothing about which reply is
the answer; the answer is always the last turn's reply. Middleware added in `Configure` wraps the whole loop, and a `SteppedAgent`
already holds the builder's one step loop, so a second step loop in its `Configure` fails the build. A state read before
`StartAsync` set it fails with a message that says so.

Mistakes name the class and the fix: no role anywhere, blank attribute text, or anything the builder refuses in `Configure`.
A derived class that overrides `Configure` without calling `base.Configure` drops what the base class assigned there.

## Approvals

Mark a tool `[Tool(..., RequiresApproval = true)]` (see [`PinkRooster.ToolCollections`](https://github.com/pinkroosterai/PinkRooster/blob/main/docs/PinkRooster.ToolCollections.md)), or call `RequireApproval("RunShell")` on the
builder. A run that calls such a tool ends with a `ToolApprovalRequestContent`; the answer goes back on the same session and the
run continues. `RunWithApprovalsAsync` is that loop: it asks your callback about each call and repeats until a run ends without asking.

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents;

AgentSession session = await agent.CreateSessionAsync();
AgentResponse response = await agent.RunWithApprovalsAsync("Clean the build output.", session, AskTheUserAsync);
```

`AskTheUserAsync` stands for your own method: it gets the `FunctionCallContent` and a cancellation token, and returns true to let the call run; a refused call is not run, and the model is told the user
did not allow it. `AgentConsole.ConfirmToolCallAsync` of `PinkRooster.SpectreConsole` fits as it is. `RunStreamingWithApprovalsAsync`
is the same loop for a streamed run. To answer in a later request instead, as a web app does, run the agent yourself:
`response.GetApprovalRequests()` lists what a response asks, each request's `CreateResponse(approved)` makes its answer, and all
the answers of one response go back together in one user message on the same session.

### A permission policy

Asking about every call teaches a user to stop reading what they approve. `PermissionPolicy` answers what a mode and a list of
allow rules already decide, and asks only about the rest:

```csharp
using Microsoft.Agents.AI;
using PinkRooster.Agents;
using PinkRooster.Agents.Permissions;
using PinkRooster.SpectreConsole;
using PinkRooster.ToolCollections.BuiltIn;
using PinkRooster.ToolCollections.BuiltIn.FileOperations;
using PinkRooster.ToolCollections.BuiltIn.Shells;
using Spectre.Console;

AgentConsole console = new(AnsiConsole.Console);
Workspace workspace = new(repoRoot);
FileOperationsToolCollection files = new(workspace);
ShellToolCollection shell = new(workspace);

PermissionPolicy policy = new PermissionPolicy(console.ConfirmToolCallAsync)
    .WithTools(files, shell)
    .WithCommandArgument("RunShell", "command")
    .Allow(AllowRule.ForPrefix("RunShell", "command", "git status"));

AIAgent agent = chatClient.CreateAgent()
    .WithRole("You are a coding assistant.")
    .WithTools(files, shell)
    .RequireApproval("RunShell", "CreateFile", "EditFile", "MoveFile")
    .WithConsole(console)
    .Build();

AgentResponse response = await agent.RunWithApprovalsAsync("Fix the failing test.", session: null, policy.AnswerAsync);
```

The policy is given the function that asks the user, and the tools, whose [kind](https://github.com/pinkroosterai/PinkRooster/blob/main/docs/PinkRooster.ToolCollections.md#tool-kinds) it decides by:

| `policy.Mode` | `Read`, `State` | `Edit` | `Execute` | no kind |
|---|---|---|---|---|
| `Ask` (the default) | allowed | asks | asks | asks |
| `AcceptEdits` | allowed | allowed | asks | asks |
| `Plan` | allowed | refused | refused unless a rule given with `Allow` covers the call | the same |

- **It only answers calls that need approval.** A tool that does not need it runs without the policy ever seeing the call, so mark
  every tool that changes something: `RequiresApproval`, or `RequireApproval(...)` on the builder as above.
- **No mode allows a tool without a kind**, and a tool the policy was not told has none. Only a rule that names it, or the user, lets it run.
- **A refusal by the mode tells the model why.** In `Plan` the tool's answer says the permission mode is plan, so the model writes the
  change into its plan instead of trying again. A call the user skips still says the user did not allow it.
- **An allow rule** covers every call of a tool (`AllowRule.ForTool`), the calls whose text argument starts with a prefix
  (`ForPrefix`), or the calls whose text argument is exactly one text (`ForWholeText`). A prefix matches whole words, ignoring case,
  and never a text that holds ``; & | ` ( ) < >`` or a line break: `dotnet build; rm -rf .` is asked about, whatever the rule. A whole
  text may chain, because you wrote all of it. A prefix says what a command starts with, not what it does.
- **The user's third answer.** Besides allow and skip, the question offers a rule for the rest of the session
  (`ApprovalChoice.AllowForSession`): the whole tool, or for a tool named in `WithCommandArgument` the start of the command, which
  is its first two words when the second is a subcommand and else its first (`dotnet build -c Release` offers `dotnet build`). A
  command that chains is offered no rule. `policy.SessionRules` lists what the user added, `policy.Rules` what you gave.
- **`Plan` honours only your rules**, never one the user added at a prompt, and never for a tool that edits.
- **`policy.Reset()`** goes back to `Ask` and drops the user's rules, for a host that starts or resumes a conversation.
- **Sub-agents.** Give the same `policy.AnswerAsync` to `ApproveToolCallsWith`, and tell the policy the tools of the tool sets too.
  One policy then answers the agent and its sub-agents, and asks one question at a time.
- **Your own callback** can refuse with a reason too: return `ApprovalAnswer.Refuse("Tickets are frozen until Monday.")` from a
  callback that returns `Task<ApprovalAnswer>`.
- **With MAF's `ToolApprovalAgent`.** It works below the policy: a call its own standing rules approve never reaches the policy, so
  it runs in `Plan` too. Use one of the two for standing approvals.

**A callback can decide by what a tool does.** Every tool of the ready-made collections declares a
[kind](https://github.com/pinkroosterai/PinkRooster/blob/main/docs/PinkRooster.ToolCollections.md#tool-kinds) (`Read`, `State`, `Edit` or `Execute`), and `tool.GetKind()` reads it from a tool, also from
one that needs approval or runs in the background. Of the tools this package adds, `RunSubAgent` and `CancelTask` are `State`;
`WaitForTasks`, `GetTaskResult` and `ListTasks` are `Read`.

**`RunShell` runs unattended unless you ask for approval.** See
[`PinkRooster.ToolCollections.BuiltIn`](https://github.com/pinkroosterai/PinkRooster/blob/main/docs/PinkRooster.ToolCollections.BuiltIn.md).

## Events

`OnEvent` reports what the agent is doing as it happens. It works the same for `RunAsync` and `RunStreamingAsync`:

<!-- events:begin -->
```csharp
using Microsoft.Agents.AI;
using PinkRooster.Agents;
using PinkRooster.Agents.Eventing;

AIAgent agent = chatClient
    .CreateAgent()
    .WithRole("You triage support tickets.")
    .WithTools(new TicketTools(store))
    .OnEvent(item =>
    {
        switch (item)
        {
            case ToolCallStarted call: Console.WriteLine($"-> {call.Name}"); break;
            case ToolCallCompleted done: Console.WriteLine($"<- {done.Name}: {done.Status}"); break;
            case AssistantTextDelta text: Console.Write(text.Text); break;
        }
    })
    .Build();

await agent.RunAsync("Who has PR-7?");
```
<!-- events:end -->

It prints:

<!-- events-output:begin -->
```text
-> GetTicket
<- GetTicket: Succeeded
PR-7 is open and assigned to Ana.
```
<!-- events-output:end -->

A fuller handler, with reasoning and the time each tool took:

```csharp
using Microsoft.Agents.AI;
using PinkRooster.Agents;
using PinkRooster.Agents.Eventing;
using PinkRooster.ToolCollections.BuiltIn.Shells;

AIAgent agent = chatClient.CreateAgent()
    .WithRole("You are a build assistant.")
    .WithTools(new ShellToolCollection())
    .OnEvent(item =>
    {
        switch (item)
        {
            case ReasoningDelta thought: Console.Write(thought.Text); break;
            case ToolCallStarted call: Console.WriteLine($"-> {call.Name}"); break;
            case ToolCallCompleted done: Console.WriteLine($"<- {done.Name}: {done.Status} in {done.Duration}"); break;
            case AssistantTextDelta text: Console.Write(text.Text); break;
        }
    })
    .Build();
```

Every event carries its `RunId`, the agent's ID and name, and a timestamp. `OnEvent<ToolCallCompleted>(done => ...)` hears one event type, and
the types derived from it, without a type switch.

| Event | Published when |
|---|---|
| `RunStarted`, `RunCompleted` | A run starts, and ends. `RunCompleted` carries the outcome (`Succeeded`, `Failed`, `Cancelled`, `AwaitingApproval` or `Abandoned`), the duration and the usage summed over the run's own model calls. |
| `ModelCallStarted`, `ModelCallCompleted` | Each model call; the second carries the model ID, finish reason, usage (null when the provider sent none) and duration, or the error. |
| `ReasoningDelta`, `ReasoningCompleted` | Reasoning text arrives, as pieces and as one completed block. |
| `AssistantTextDelta`, `AssistantTextCompleted` | Answer text arrives, as pieces and as one completed block (`Text`). |
| `ToolCallRequested`, `ToolApprovalRequested` | The model asks for a tool; the tool needs approval first. |
| `ToolCallStarted`, `ToolCallCompleted` | A tool runs, and ends as succeeded, failed, rejected or cancelled. |
| `StepStarted`, `StepChecked` | A step of a stepped agent begins; a review or check of its reply has a result. |
| `BackgroundTaskStarted`, `BackgroundTaskEnded` | A tool call began as a background task; it ended as completed, failed, cancelled or timed out. See [Background tools](#background-tools). |
| `BackgroundWaitStarted`, `BackgroundWakeLimitReached` | The run waits for background tasks without calling the model; it stopped waiting at its limit on wakes. |

What a handler can rely on:

- **One start, one end.** Each run publishes `RunStarted` first and exactly one `RunCompleted` last, whatever its outcome. `Abandoned` is a streamed run whose consumer stopped reading before the end.
- **Tool calls pair up.** Every `ToolCallStarted` gets exactly one `ToolCallCompleted`.
- **Model calls pair up.** Each model call is bracketed by `ModelCallStarted` and one `ModelCallCompleted`.
- **Usage stays with its run.** An agent called as a tool (`otherAgent.AsAIFunction()`) reports its own usage on its own `RunCompleted`, and its events reach its caller's handlers too, with `ParentRunId` set to the caller's run.
- **Handlers run synchronously**, in the order added, on the thread that produced the event; parallel tool calls can call them from several threads at once.
- **A handler that throws is always caught and logged.** The run goes on and the tool's result is never changed. With `WithLoggerFactory` set the exception is logged there; without it the failure is not logged anywhere. An `OperationCanceledException` counts too, because a handler has no cancellation token, so a handler's own timeout is not a cancelled run.

`Build()` returns a thin wrapper around the `ChatClientAgent`; `agent.GetService<ChatClientAgent>()` returns the inner agent.

### Your own middleware

`Use` adds agent middleware, MAF's word and MAF's shape: a callback that gets the agent so far and a service provider (the one given to
`WithServices`, or an empty one) and returns an agent that wraps it, inside the event layer. Middleware applies in the order added, so the
first call sits nearest the agent. Inside a run, `AgentEvents.Publish(item)` sends an event to the agent's handlers with the run's `RunId`
filled in, and returns false when no run is in progress:

```csharp
using Microsoft.Agents.AI;
using PinkRooster.Agents;
using PinkRooster.Agents.Eventing;

public sealed record Escalated(string Reason) : AgentEvent;

AIAgent agent = chatClient.CreateAgent()
    .WithRole("You triage support tickets.")
    .Use((inner, services) => new EscalationAgent(inner))   // calls AgentEvents.Publish(new Escalated(...))
    .OnEvent(item => { if (item is Escalated escalated) Console.WriteLine(escalated.Reason); })
    .Build();
```

You can publish `StepStarted`, `StepChecked` and events you derive from `AgentEvent` yourself. The events the agent publishes about runs, model
calls, tool calls, reasoning and answer text are refused with an `ArgumentException`, so every run keeps exactly one `RunCompleted`. A callback
that returns null makes `Build()` throw. MAF's own middleware goes in the same way, such as `.Use((inner, _) => new ToolApprovalAgent(inner))`;
the `ToolApprovalRequested` event is then published once for each request MAF shows the caller.

## Sub-agents

`SubAgentToolCollection` gives an agent one tool, `RunSubAgent`. With it the agent hands a task to a helper that starts with an
empty context, and gets back only the helper's answer: the helper's tool calls and long outputs never enter the agent's own
context. The agent writes the helper's brief itself (a role, instructions, an output format and the prompt) and picks the model
and the tools from lists you supply.

```csharp
using Microsoft.Agents.AI;
using PinkRooster.Agents;
using PinkRooster.Agents.SubAgents;
using PinkRooster.ToolCollections.BuiltIn.FileOperations;
using PinkRooster.ToolCollections.BuiltIn;
using PinkRooster.ToolCollections.BuiltIn.Shells;

var workspace = new Workspace(repoRoot);
SubAgentToolCollection subAgents = new SubAgentToolCollectionBuilder()
    // Each model has its own IChatClient; a cheaper one for routine work is the usual reason to list two.
    .WithModel("small", "Searching, reading and summarising.", chatClient)
    .WithModel("large", "Hard reasoning and review.", chatClient)
    .WithToolSet("files", "Reading, searching and changing files.", new FileOperationsToolCollection(workspace))
    .WithToolSet("shell", "Running builds and tests.", new ShellToolCollection(workspace))
    .Build();

AIAgent agent = chatClient
    .CreateAgent()
    .WithRole("You maintain this repository.")
    .WithTools(subAgents)
    .Build();
```

- **Models and tool sets** are named, and each has a `UseWhen` line. The agent reads both lists in its system prompt and names
  what a sub-agent gets; a sub-agent has only the tools of the sets named for it, and none when no set is named. A tool set
  holds tool collections, plain tools, or both: `WithToolSet` takes collections, a list of `AITool`s, or a `SubAgentToolSet` that has both.
- **Every call is a new sub-agent.** It sees the brief and the prompt and nothing of the conversation, keeps nothing for a later
  call, and cannot start sub-agents itself.
- **The result is the answer's text**, at most 20,000 characters; a longer answer is cut in the middle. `WithMaxResultCharacters` changes the limit.
- **Mistakes and failures come back as text** that starts with `Error:` and says what to change: an unknown model or tool set,
  a blank role or prompt, named sets whose tools share a name, or a model call that failed. Only a cancellation throws.
- **Events.** A sub-agent's events reach the calling agent's `OnEvent` handlers with `ParentRunId` set and the sub-agent's name
  as `AgentName`; its token usage is on its own `RunCompleted`. `AgentConsole` draws them as labelled lines under the `RunSubAgent` call.
- **`ConfigureSubAgents`** runs last on every sub-agent's builder, for what the brief does not cover:
  `.ConfigureSubAgents(builder => builder.WithLoggerFactory(loggers).ConfigureToolLoop(loop => loop.MaximumIterationsPerRequest = 15))`.
  A sub-agent's run has no time limit of its own; it ends with its answer, at its tool loop's limit, or when the call is cancelled.
- **Approvals.** A sub-agent's run cannot stop to ask you the way your own agent's run does. Call `ApproveToolCallsWith` with a function
  that is shown one tool call and returns true to let it run; `AgentConsole.ConfirmToolCallAsync` fits as it is. It is asked about one call
  at a time. A [permission policy](#a-permission-policy)'s `AnswerAsync` fits too, and its refusal tells the sub-agent why. Without it, every call to a tool that needs approval is rejected and the sub-agent goes on without that tool.
- **`RunSubAgent` itself needs no approval.** Add `.RequireApproval("RunSubAgent")` to see each delegation first.
- **Without the builder**, `new SubAgentToolCollection(models)` takes a list of `SubAgentModel` and gives sub-agents without tools; tool sets, the result limit and approvals are the builder's.

## Background tools

A tool that takes long, such as a sub-agent run or a build, keeps the agent standing still until it returns. `AllowBackground` lets
the model start such a call as a background task instead: the call returns a task id at once, the model goes on with other work,
and it reads the result when the task has ended.

```csharp
using Microsoft.Agents.AI;
using PinkRooster.Agents;
using PinkRooster.ToolCollections.BuiltIn;
using PinkRooster.ToolCollections.BuiltIn.Shells;

AIAgent agent = chatClient
    .CreateAgent()
    .WithRole("You maintain this repository.")
    .WithTools(new ShellToolCollection(new Workspace(repoRoot)))
    .AllowBackground("RunShell")
    // Optional: every limit has a default.
    .ConfigureBackground(limits =>
    {
        limits.MaxRunningTasks = 3;
        limits.TaskTimeLimit = TimeSpan.FromMinutes(5);
    })
    .Build();
```

- **The model chooses per call.** Each named tool gets one more optional parameter, `runInBackground`. A call without it runs and
  answers as before. A call with it answers `Started task 3 (RunShell). ...` and the tool runs on.
- **The agent gets four tools** to follow its tasks, in `BackgroundTasksToolCollection`:
  - `WaitForTasks` waits for the tasks it is given and returns how each stands, with the result of every one that has ended. It
    returns when the first has ended, or with `waitForAll` when all have; also at its timeout, and when you post a message.
  - `GetTaskResult` reads one task's result again.
  - `ListTasks` lists the run's tasks, and `CancelTask` stops one.

  A result is text, cut in the middle past 20,000 characters; the limit is for one answer, shared by the results in it. Every answer
  names the task's status in words: running, completed, failed, cancelled or timed out.
- **Any tool can be named**, and `SubAgentToolCollection` is not a special case. Naming a tool says it may run at the same time as
  other calls on the same collection instance. The builder cannot check that: read the collection before you allow a tool that keeps
  state per instance. The [built-in collections guide](https://github.com/pinkroosterai/PinkRooster/blob/main/docs/PinkRooster.ToolCollections.BuiltIn.md) says which of its tools are safe to allow.
- **A task belongs to the run that started it.** The run stays alive while a task runs: when the model ends its turn early it is not
  called again until a task ends or a message arrives, and `RunAsync` returns the last reply. When the run ends, because it was
  cancelled, failed or reached its limit on wakes, its running tasks are cancelled and waited for. Nothing survives the process.
  A session runs one such run at a time; a second `RunAsync` on it while one is in progress throws.
- **Task ids count on within a session.** A later run's first task is not `task 1` again, so an id in the history never comes to
  mean another task. A run's tasks and their results are gone when it ends.
- **The model is told once** when a task ends: by the tool answer that reports it, or else by a short message in its inbox such as
  `Background task 3 (RunShell) completed. ...` The message never holds the result.
- **A tool that throws** ends its task as failed. The exception is logged and is on the `BackgroundTaskEnded` event. The model reads
  its message only when the tool loop shows error details (`ConfigureToolLoop(loop => loop.IncludeDetailedErrors = true)`), as for a
  foreground call.
- **Approvals.** A tool that needs approval is approved first and then starts as a task. A run that ends with an approval request is
  paused: its tasks keep running, and the run that carries the answers on the same session takes them over. If that run's input holds
  anything besides approval answers, the paused run's tasks are cancelled first. A background sub-agent's own approvals go to
  `ApproveToolCallsWith`, as in the foreground.
- **Events.** `BackgroundTaskStarted` and `BackgroundTaskEnded` carry the task id and the tool name; the second also the state and
  the result or exception. `BackgroundWaitStarted` says the run now waits for tasks without calling the model, and
  `BackgroundWakeLimitReached` that it stopped waiting. `AgentConsole` draws a line for each. Task events carry the `RunId` of the run
  that started the task, so a task that ends during an approval pause publishes its `BackgroundTaskEnded` after that run's `RunCompleted`.
- **Agent classes** name the tools with `[AgentAllowBackground("RunShell")]`, the attribute form of `AllowBackground`.
- **Mistakes fail at `Build()`**, naming the fix: a name that matches no tool, a tool that already has a `runInBackground` parameter,
  `ConfigureBackground` without a background tool or an inbox, a limit that cannot work, or a `ConfigureAgentOptions` callback that
  switched off what the inbox needs. `BuildOptions()` refuses a builder with background tools or an inbox, because both are kept by
  the agent `Build()` makes.

`ConfigureBackground` changes the limits. Each default is a figure a shipped harness or MAF uses; none was measured for this library.

| Limit | Default | Where the figure comes from |
|---|---|---|
| `MaxRunningTasks` | 6 | Codex's default cap on agent threads |
| `TaskTimeLimit` | 30 minutes | Claude Code's limit for a background command in an unattended session |
| `GracePeriod` | 30 seconds | MAF's default when it releases background agents |
| `MaxResultCharacters` | 20,000 | `SubAgentToolCollection`'s result limit |
| `DefaultWaitTimeout` | 300 seconds | MAF's default wait for a background agent |
| `MaxWaitTimeout` | 1 hour | Codex's longest wait |
| `MaxWakes` | 10 | MAF's default number of loop iterations |

A start beyond `MaxRunningTasks` starts nothing and answers with an error the model reads. A task past `TaskTimeLimit` is stopped and
ends as timed out. A tool that does not stop within `GracePeriod` after it was asked to is reported as not having stopped, and the run
no longer waits for it. Until then it holds up whoever stopped it: a cancelled `RunAsync` returns that much later, and so does the
model's `CancelTask` call. Give long-running tools a `CancellationToken` and honour it.

`MaxWakes` is how often one run may call the model again because a task ended after the model had ended its turn. A call made for a
message you posted is not counted. At the limit the run ends with its last reply and its running tasks are cancelled; a
`BackgroundWakeLimitReached` event and a logged warning say so. With `MaxWakes = 0` a run never waits for its tasks.

### Posting a message to a running agent

`WithInbox()` gives an agent an inbox, and so does `AllowBackground`. `PostMessageAsync` puts a message in it while a run is in
progress, from any thread:

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents;

AgentSession session = await agent.CreateSessionAsync();
Task<AgentResponse> run = agent.RunAsync("Run the full test suite and fix what fails.", session);
await agent.PostMessageAsync(session, new ChatMessage(ChatRole.User, "Skip the integration tests."));
AgentResponse response = await run;
```

The model reads the message before its next model call, and it stays in the session's history. A model that had ended its turn while
tasks run is called again for it, and a waiting `WaitForTasks` returns. `PostMessageAsync` throws when no run is in progress on the
session; pass the message as the next run's input then. `TryPostMessageAsync` returns false instead of throwing, for a host that keeps
what it could not post, and `agent.HasInbox()` says whether an agent has one. What you post, and in which role, is yours to decide:
the model reads it as part of the conversation.

Things to know:

- **`ConfigureToolLoop` delays the inbox by one model call.** MAF places its message injection above a tool loop the builder supplies,
  so a posted message or task update is read when that loop's turn has ended, not before its next model call. Nothing is lost or
  repeated, and `Build()` logs a warning. `[AgentToolLoop]` on an agent class does the same.
- **History is saved after every model call** for an agent with an inbox, not once at the end of the run. That is what keeps an inbox
  message in the session's history. A chat history provider is therefore called once per model call.
- **A post is refused once the run is ending.** When the model has ended its turn, no task runs and the inbox is empty, the run
  closes its inbox before it ends, so a message can never be accepted with nobody left to read it: `TryPostMessageAsync` returns
  false and `PostMessageAsync` throws.
- **A post while a run waits for an approval is accepted**, and the run that carries the answers reads it.
- **A run that is cancelled or fails can leave an accepted message unread.** It stays in the session, and the session's next run
  reads it.
- **A tool call that never got its result is answered as cancelled.** Because history is saved after every model call, a run
  cancelled while a tool runs leaves the call in the history without a result, which a provider rejects. Every request of an
  agent with an inbox therefore gives such a call a result that says it was cancelled and that its effects are unknown. The stored
  history is not changed.
- **Typing while a run is in progress** is `RunWithInputAsync` of [`PinkRooster.SpectreConsole`](https://github.com/pinkroosterai/PinkRooster/blob/main/docs/PinkRooster.SpectreConsole.md#type-while-the-agent-runs), which posts each typed line to this inbox.

## Saving and resuming a session

`SessionStore` keeps an agent's sessions as files in a folder you name, so a conversation can be gone on with after the program
has ended:

```csharp
using Microsoft.Agents.AI;
using PinkRooster.Agents.Sessions;

SessionStore store = new(Path.Combine(repoRoot, ".pinkrooster", "sessions"));

AgentSession session = await agent.CreateSessionAsync();
string prompt = "Why does the build fail?";
await agent.RunAsync(prompt, session);
await store.SaveAsync(agent, session, prompt);          // after every run

foreach (SavedSession saved in store.List())            // the one saved last first
{
    Console.WriteLine(saved.IsReadable ? $"{saved.Id}  {saved.SavedAt:g}  {saved.FirstPrompt}" : $"{saved.Id}  unreadable: {saved.Problem}");
}

RestoredSession restored = await store.RestoreAsync(agent, store.List()[0].Id);
await agent.RunAsync("Go on.", restored.Session);
```

- **One file per session.** The first save gives the session an id and keeps that run's prompt as its first prompt; every later
  save replaces the same file, also for a session that was restored. Save after a run has ended: the file is then the session as
  it was after the last run, and a reader never sees half a file.
- **What is in it.** What MAF's `SerializeSessionAsync` returns (the history, a step loop's place, a per-session task list and other
  collection state), with the first prompt, the time and the names of the agent's tools. It is plain JSON with the whole
  conversation in it, tool results included: keep the folder out of version control.
- **Restore with the agent that saved it**, configured the same way. A restored session's next request holds the saved messages
  followed by the new prompt.
- **When the tools changed, it restores anyway and says what differs.** `restored.MissingTools` are the tools the session was saved
  with that the agent no longer has, `restored.NewTools` the ones it has now. A call the model makes to a tool that is gone is
  answered as an unknown tool. The tool names come from the agent's chat options, so an agent the builder did not make has none
  on record.
- **A file that cannot be read stays where it is.** `List` shows it with `IsReadable` false and the reason; `RestoreAsync` throws
  for it. The store never deletes a file.
- **Background tools and an inbox are no obstacle**: a session of a stepped agent with both is restored and gone on with, which a test
  in the repository shows. A task cannot be saved while it runs, and a run does not end while one does.

## Agents the builder did not make

Give collections to any MAF agent, including `HarnessAgent` (package `Microsoft.Agents.AI.Harness`), through the context provider:

```text
dotnet add package Microsoft.Agents.AI.Harness
```

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.ToolCollections.BuiltIn.Shells;
using PinkRooster.ToolCollections.Context;

AIAgent agent = chatClient.AsHarnessAgent(new HarnessAgentOptions
{
    AIContextProviders = [new ToolCollectionContextProvider(new ShellToolCollection())],
});
```

The provider sends the tools, their standing text and their context, once per run. For context on
every model call of a hand-built `ChatClientAgent`, put the collections in its client with
`chatClient.AsBuilder().UseToolCollections(collections).Build()` and pass their tools yourself.
Use one of the two per agent, not both, or the context arrives twice.

## Things to know

- **Tool names must be unique per agent.** The builder checks the tools it knows about. It cannot
  see tools that a context provider or MAF adds at run time, so a clash with one of those shows up
  only when the agent runs.
- **Defaults are per agent.** `WithDefaults(...)` and `WithoutDefaults()` change them for one agent; to change them for every agent, build one `AgentDefaults` and pass it from the method all your agent factories call.

## Feedback and license

Report a bug or ask a question on [GitHub Issues](https://github.com/pinkroosterai/PinkRooster/issues). The package is released under the [MIT license](https://github.com/pinkroosterai/PinkRooster/blob/main/LICENSE).
