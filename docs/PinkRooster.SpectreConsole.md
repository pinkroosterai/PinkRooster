# PinkRooster.SpectreConsole

Shows an AI agent's run on a [Spectre.Console](https://spectreconsole.net) terminal: its reasoning, steps and tool calls with their timing, the answer as formatted markdown while the model writes it, and the prompts that ask you to approve a tool call or answer the agent's question.

```text
dotnet add package PinkRooster.SpectreConsole --prerelease
```

Needs the .NET 10 SDK, a terminal and an `IChatClient` for your model (`chatClient` below; [how to make one](https://github.com/pinkroosterai/PinkRooster/blob/main/docs/PinkRooster.Agents.md#quickstart)). `ReviewerAgent` stands for an agent class of your own, like the ones in the agent guide's [Agent classes](https://github.com/pinkroosterai/PinkRooster/blob/main/docs/PinkRooster.Agents.md#agent-classes). It builds on `PinkRooster.Agents` (the events it draws) and `PinkRooster.ToolCollections.BuiltIn` (the `AskUserQuestion` models), with `Spectre.Console` and `NTokenizers.Extensions.Spectre.Console` for the drawing.

## Show a run

Give an `AgentConsole` the agent's events (`reviewer` is an agent class, as in the agent guide's [Agent classes](https://github.com/pinkroosterai/PinkRooster/blob/main/docs/PinkRooster.Agents.md#agent-classes)):

```csharp
using PinkRooster.Agents;
using PinkRooster.SpectreConsole;
using Spectre.Console;

AgentConsole console = new(AnsiConsole.Console);
reviewer.OnEvent(console.WriteEvent);

await foreach (var update in reviewer.RunStreamingAsync("What changed in the last release?"))
{
    // the console draws each update as it arrives
}
```

For an agent class, the same in one chain; `RunToConsoleAsync` streams the run, which the console draws, and returns the whole response:

```csharp
using PinkRooster.Agents;
using PinkRooster.SpectreConsole;
using Spectre.Console;

await using ReviewerAgent reviewer = chatClient.CreateAgent<ReviewerAgent>().WithConsole(AnsiConsole.Console);
await reviewer.RunToConsoleAsync("What changed in the last release?");
```

`WithConsole` makes one `AgentConsole` for that agent. Without `WithConsole` (or an `OnEvent` handler of your own), `RunToConsoleAsync` draws nothing.

For an agent from the builder, keep the console and give it to both: `WithConsole` draws the runs, and `RunToConsoleAsync` with the
console asks you to confirm each tool call that needs approval and continues the run with your answers:

```csharp
using Microsoft.Agents.AI;
using PinkRooster.Agents;
using PinkRooster.SpectreConsole;
using PinkRooster.ToolCollections.BuiltIn.Shells;
using Spectre.Console;

AgentConsole console = new(AnsiConsole.Console);
AIAgent agent = chatClient
    .CreateAgent()
    .WithRole("You are a build assistant for this repository.")
    .WithTools(new ShellToolCollection())
    .RequireApproval("RunShell")
    .WithConsole(console)
    .Build();

await agent.RunToConsoleAsync("Does the solution build?", console);
```

To ask less, give the run a [permission policy](https://github.com/pinkroosterai/PinkRooster/blob/main/docs/PinkRooster.Agents.md#a-permission-policy) made with the console:
`PermissionPolicy policy = new(console.ConfirmToolCallAsync);` then `await agent.RunToConsoleAsync("Does the solution build?", policy);`.
The prompt then offers a third answer, such as `Allow for this session: RunShell starting with "dotnet build"`, and the policy keeps it for the runs that follow.

It draws reasoning grey under a `Thinking:` label, each step and check, each tool call with its arguments, then a mark, its time and the first lines of what it returned, and the answer. Nested agents' events are not drawn.

- **One run at a time.** The console keeps the line it is drawing. Two runs that overlap on one console mix their text; give each run its own console, or draw them one after the other.
- **The answer streams.** On a terminal with ANSI that is interactive, answer text is drawn as markdown while it arrives: headings, lists, tables and code blocks. Anything else the console draws, such as a tool call or a prompt, waits until the answer is complete on the screen. On any other terminal the answer is written as plain indented lines as it arrives.
- **Secrets stay off the screen.** Values in `AgentConsoleOptions.SensitiveValues` are drawn as `[redacted]` in text, arguments, results and answers, also when one arrives split across two pieces of text; the console holds back the last few characters of the stream to see that.

## Type while the agent runs

`RunWithInputAsync` runs the agent as `RunToConsoleAsync` does and keeps an input line open meanwhile. A line you submit is posted
to the agent's inbox and read by the model before its next model call; Esc stops the run:

```csharp
using Microsoft.Agents.AI;
using PinkRooster.Agents;
using PinkRooster.SpectreConsole;
using PinkRooster.ToolCollections.BuiltIn.Shells;
using Spectre.Console;

AgentConsole console = new(AnsiConsole.Console);
AIAgent agent = chatClient
    .CreateAgent()
    .WithRole("You are a build assistant for this repository.")
    .WithTools(new ShellToolCollection())
    .WithInbox()
    .WithConsole(console)
    .Build();
AgentSession session = await agent.CreateSessionAsync();

string? prompt = await console.ReadLineAsync("You: ", CancellationToken.None);
while (prompt is not null)
{
    ConsoleRunResult run = await agent.RunWithInputAsync(prompt, console, session);
    // A line that came too late for the run is the next prompt; nothing typed is lost.
    prompt = run.UnreadLines.Count > 0 ? run.UnreadLines[0] : await console.ReadLineAsync("You: ", CancellationToken.None);
}
```

- **The agent needs an inbox**: `WithInbox()`, or `AllowBackground`, which gives it one too. Without one the call throws and names both.
- **Every submitted line is delivered once.** It is drawn as `queued` and posted as soon as a run can read it. A line that came after
  the last model call is not posted: it comes back in `UnreadLines`, in the order typed, for you to send as the next prompt.
  `Draft` is what was in the input line and not submitted.
- **A typed line never interrupts a tool call.** The model reads it when the call has returned.
- **Lines that are not for the model.** `new ConsoleRunOptions { HoldLine = line => line.StartsWith('/') }` keeps such a line away from
  the model: it is drawn as `held` and comes back in `UnreadLines` when the run has ended, for your own command handling.
- **Esc stops the run** and returns with `Cancelled` set and the session as it stands; your own cancellation token is not cancelled, and
  cancelling that one throws as usual, so what Ctrl+C does stays yours to decide. A tool call that was running is answered as
  cancelled in the next request. A line the inbox had taken that the model had not read yet stays in the session, and its next
  run reads it.
- **A prompt owns the keyboard.** While an approval or a question is asked, keys go to that prompt. A prompt waits until the
  keyboard has been quiet for a moment before it opens, so the rest of a line you were typing, and its Enter, are not taken as the answer.
- **Where the input line is.** It is the last row of the screen, taken away before anything else is drawn and put back after. While a
  streamed line is half drawn, the live answer included, it is off the screen; keys typed then are kept, and the line and any
  `queued` notice appear when the answer is complete.
- **A permission policy** goes in `ConsoleRunOptions.Policy`, and answers the run's approvals.
- **On a terminal that is not interactive, or without ANSI, nothing changes**: the run takes no input, and the agent needs no inbox.
  The same goes for a console that is not an `AgentConsole`.

## Ask the user

```csharp
using Microsoft.Extensions.AI;
using PinkRooster.SpectreConsole;
using Spectre.Console;

AgentConsole console = new(AnsiConsole.Console);

bool allowed = await console.ConfirmToolCallAsync(new FunctionCallContent("call-1", "DeleteFile"), CancellationToken.None);
string? line = await console.ReadLineAsync("You: ", CancellationToken.None);
```

`ConfirmToolCallAsync` shows a panel with the call and asks Allow or Skip. The panel names the tool and draws one argument per row, as the text it is and not as JSON: a script keeps its lines, up to `AgentConsoleOptions.MaxApprovalLines` (40), and the rest is counted. A tool-call line draws the same arguments on one row, as `name: value, name: value`. It it returns false when the user skips or the terminal is not interactive. Given an `ApprovalQuestion` instead of a call, it returns an `ApprovalChoice`, and when the question carries a `SessionRule` it offers that rule as a third answer; this is the one member of `IAgentConsole` for approvals, and the yes-or-no form is an extension method on it, so it needs `using PinkRooster.SpectreConsole;`. `AskAsync` takes the `UserQuestion`s of the built-in `AskUserQuestion` tool and returns one answer each, single or multiple choice with an "Other" free-text choice; it throws `InvalidOperationException` on a terminal that cannot ask. `ReadLineAsync` returns null when input is redirected, ended or cancelled.

The safe prompt also stands alone, on any `IAnsiConsole`:

```csharp
using PinkRooster.SpectreConsole;
using Spectre.Console;

string? name = await AnsiConsole.Console.AskOrNullAsync("Your name: ", CancellationToken.None, style: "bold");
bool canDrawMarkdown = AnsiConsole.Console.CanRenderMarkdown();
```

## Colours and limits

```csharp
using PinkRooster.SpectreConsole;
using Spectre.Console;

AgentConsole console = new(AnsiConsole.Console, new AgentConsoleOptions
{
    Accent = "blue",
    MaxResultLines = 5,
    SensitiveValues = [Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? ""],
});
```

`Accent`, `Muted`, `Success` and `Failure` are Spectre.Console colours; `Indent`, `MaxLineLength` (160) and `MaxResultLines` (10) bound what is drawn; `ShowNestedRuns` (on) draws the runs of sub-agents. Code written against `IAgentConsole` runs with `AgentConsole` or with any other implementation, such as one that records what it was asked to show.

## Draw a whole answer

When you have the answer as a string, such as `response.Text`:

```csharp
using PinkRooster.SpectreConsole;
using Spectre.Console;

var response = await agent.RunAsync("What changed in the last release?");
AnsiConsole.Console.WriteAnswer(response.Text);
```

`AgentConsole.WriteAnswer` does the same after ending any streamed answer and with secrets redacted. An app that only wants this still restores `PinkRooster.Agents` and `PinkRooster.ToolCollections.BuiltIn` with the package.

## Good to know

- **Sub-agents and other nested runs.** A run that the agent starts through a tool, such as a sub-agent of `SubAgentToolCollection` or another agent given as a tool, is drawn under that tool call: its start and end (with time and tokens), its tool calls, and one line of each block of reasoning, indented and labelled with the agent's name. Its answer and its tools' results are not drawn; the answer appears as the tool call's result. These are whole lines, never a stream, so sub-agents that run at the same time stay readable. `ShowNestedRuns = false` draws the outer run only.
- **Background tasks.** A tool call that an agent started in the background (see [background tools](https://github.com/pinkroosterai/PinkRooster/blob/main/docs/PinkRooster.Agents.md#background-tools)) is drawn as one line when it starts and one when it ends, with the task's id, its tool and how it ended: `▸ task 3 (RunSubAgent) started in the background`, then `✓ task 3 (RunSubAgent)  done in 4.2 s`. The start line stands for the call's own result, which is not drawn. While the run waits for its tasks without calling the model it says so once: `… waiting for 2 background tasks`.

  ```text
    RunSubAgent  modelName: small, subAgentName: Finder, …
      ▸ Finder started
      Finder: Search  query: tea
      ✓ Finder: Search  done in 1.4 s
      ◂ Finder done in 6.2 s, 4,310 tokens
    ✓ RunSubAgent  done in 6.3 s
  ```
- **What it changes in the markdown.** The renderer (`NTokenizers.Extensions.Spectre.Console` 2.4.0) draws a level 1 heading as `** Title **` and drops a link's text, so `# Title` is drawn as `## Title` and `[text](url)` as `text (url)`; fenced code is left as it is. A streamed answer gets the same fixes by holding back only a heading line and an unfinished link until they are complete.
- **Where it draws markdown.** The renderer redraws tables and code blocks in place with cursor moves, so on a redirected or piped run, or a terminal that cannot redraw, the answer is written as plain text instead.
- **Renderer failures.** A failure inside the renderer, such as a console that cannot be written to, ends the process: it works on a thread-pool thread, so a call cannot catch it. Malformed markdown from a model draws without an error.
- **Name.** The method is `WriteAnswer`, not `WriteMarkdown`, so an app that also references NTokenizers gets no ambiguous call.

## Feedback and license

Report a bug or ask a question on [GitHub Issues](https://github.com/pinkroosterai/PinkRooster/issues). The package is released under the [MIT license](https://github.com/pinkroosterai/PinkRooster/blob/main/LICENSE).
