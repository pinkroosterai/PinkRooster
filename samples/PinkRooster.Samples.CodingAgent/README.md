# PinkRooster.Samples.CodingAgent

A small terminal coding assistant, of the kind you know from Claude Code or Codex, built from the PinkRooster packages. It reads
the project's instruction file, explores, plans, asks before it changes anything, edits, runs the build, keeps a task list, hands
side work to helpers, checks its own work, and picks the conversation up again the next day.

The other samples each show one feature. This one shows that the features add up to a product. It is a project of its own,
without the shared sample library, so you can copy the folder out and change the pieces.

## Feature map

Each thing the assistant does is a short, named piece on a library feature. The last column is the sample that shows that
feature alone.

| File or folder | Holds | Library feature | Its own sample |
|---|---|---|---|
| `Program.cs` | Composition and the chat loop | `RunWithInputAsync`: a streamed run with approvals, typed lines and Esc | [Events](../PinkRooster.Samples.Events/README.md) |
| `CodingAssistant.cs` | The agent class | `SteppedAgent` (the verify step), brief attributes, `Configure`, `GetContextAsync`, `OnEvent`, `[AgentRequireApproval]`, `[AgentAllowBackground]` | [AgentClasses](../PinkRooster.Samples.AgentClasses/README.md), [SteppedAgents](../PinkRooster.Samples.SteppedAgents/README.md), [BackgroundTools](../PinkRooster.Samples.BackgroundTools/README.md) |
| `Project/` | `AGENTS.md`, the git state, the config file | `WithBackground`, live context before every model call | [AgentClasses](../PinkRooster.Samples.AgentClasses/README.md) |
| `Permissions/` | The modes and the allow rules | `PermissionPolicy`, `AllowRule`, tool kinds; given to the run and to `ApproveToolCallsWith` | [Approvals](../PinkRooster.Samples.Approvals/README.md) |
| `Helpers/` | The sub-agent models and tool sets | `SubAgentToolCollectionBuilder`, `FileReadToolCollection`, `ExternalToolCollection` | [ExternalTools](../PinkRooster.Samples.ExternalTools/README.md) |
| `Commands/` | The slash commands | Plain host code; `/undo` is `FileOperationsToolCollection.UndoAsync`, `/tasks` is `TaskListToolCollection.GetTasks` | |
| `Sessions/` | Saving and resuming | `SessionStore`, `TaskListToolCollectionBuilder.PerSession()` | [SessionState](../PinkRooster.Samples.SessionState/README.md) |
| `Skills/` | Skills from `.agents/skills/` | `WithContextProvider` with MAF's `AgentSkillsProvider` | [MafPassthrough](../PinkRooster.Samples.MafPassthrough/README.md) |
| `Mcp/` | MCP servers from the config file | `McpToolCollection`, `RequireApprovalForDestructiveTools` | [Mcp](../PinkRooster.Samples.Mcp/README.md) |
| `Images/` | Looking at screenshots and other image files, through a model that can see | `ImageToolCollection` (`QueryImage`), `ImageToolCollectionBuilder` | [Imaging](../PinkRooster.Samples.Imaging/README.md) |
| `Status/` | Token usage and the status line | `OnEvent<ModelCallCompleted>` | [Events](../PinkRooster.Samples.Events/README.md) |
| `ModelSettings/` | The models and the chat client | `ReasoningFieldChatClient`; the rest is plain host code | [Reasoning](../PinkRooster.Samples.Reasoning/README.md) |
| `History/` | Compaction | `ConfigureClient` with MAF's compaction strategies | [MafPassthrough](../PinkRooster.Samples.MafPassthrough/README.md) |

The built-in collections it uses (files with backups, shell, ask-the-user, per-session task list, date and time) are put together
in `Program.cs`; [ToolCollections](../PinkRooster.Samples.ToolCollections/README.md) shows how to write one of your own.

## Try it

Run it in a repository of your own and give it a task. While it works you can type: a line is read by the model before its next
model call, and Esc stops the run.

| Command | Does |
|---|---|
| `/plan <request>` | The assistant explores and answers with a plan, changing nothing; you carry it out, revise it or leave it |
| `/mode ask`, `accept-edits`, `plan` | What runs without asking: nothing that changes something, file edits, or nothing at all |
| `/tasks` | The task list of this conversation |
| `/status` | Model, mode, allow rules, tokens |
| `/init` | The assistant explores the repository and writes `AGENTS.md` |
| `/resume` | Goes on with a saved conversation; `/clear` starts a new one |
| `/compact` | Shortens what the next request sends; the saved conversation stays whole |
| `/undo` | Takes back the file changes of the last message that changed files |
| `/diff` | The working tree's changes, untracked files included |
| `/mcp` | The MCP servers and their tools |
| `/help`, `/exit` | The list of commands; the way out |

At an approval, "Allow for this session" keeps the tool, or the start of a shell command, allowed until `/clear` or `/resume`.
After a turn that changed files the assistant runs the build and tests and fixes what fails, up to the turn limit.

`.coding-agent.json` in the workspace root is the team's part, shared through the repository:

```json
{
  "allow": [ { "tool": "RunShell", "argument": "command", "prefix": "git status" } ],
  "verifyCommand": "dotnet build && dotnet test",
  "maxTurns": 3,
  "mcpServers": [ { "name": "Docs", "url": "https://mcp.context7.com/mcp" } ]
}
```

Without the file there are no allow rules, the assistant finds the build and tests itself, a message takes at most 3 turns, and
Context7 is the one MCP server. A server that does not connect within 5 seconds is named in one line and left out.

## Run

From the directory the assistant should work in:

```text
dotnet run --project <path to>/samples/PinkRooster.Samples.CodingAgent
```

The first run creates the model settings under your application-data folder (`PinkRooster/CodingAgent/models.json`) with a local
Ollama entry and says where the file is. With several models it asks which one to run on; the others are offered to the assistant
for its helpers, each with its `useWhen` line. Give a model its `contextSize` in tokens and a long conversation is shortened when
a request passes three quarters of it.

Mark a model `"vision": true` when it accepts images, and the assistant gets `QueryImage`: it can look at a
screenshot of a failing page, a diagram or a mock-up in the workspace. The images go to that model, the assistant's own when it
is the one marked and otherwise the first that is, and only its answer in text comes back, so the assistant's model does not have
to see and no image fills its conversation. Without a marked model the assistant has no image tool; `/status` says so.

**Needs:** an interactive terminal and a model that supports tool calls

**Guide:** [The built-in tools guide](../../docs/PinkRooster.ToolCollections.BuiltIn.md)

## Making it your own

Copy the folder, rename the project and the namespace, and replace the project references with package references:

| Project reference | Package |
|---|---|
| `PinkRooster.Agents` | `PinkRooster.Agents` |
| `PinkRooster.ToolCollections.BuiltIn` | `PinkRooster.ToolCollections.BuiltIn` |
| `PinkRooster.ToolCollections.Mcp` | `PinkRooster.ToolCollections.Mcp` |
| `PinkRooster.SpectreConsole` | `PinkRooster.SpectreConsole` |
| `PinkRooster.OpenAI.Reasoning` | `PinkRooster.OpenAI.Reasoning` |

Then change the pieces, each in the one file named for it: the brief in `CodingAssistant.cs`, the rules in `Permissions/`, the
commands in `Commands/`, the tool sets in `Helpers/`.

## What it leaves out

- **A sandbox for the shell.** Commands run with your permissions. `Shell.Launcher` is the place to start the shell through a
  sandbox of your choice; see the built-in tools guide.
- **An automatic mode** in which a classifier approves actions, **web search and fetch** as built-in tools (an MCP server can
  supply them), **switching the model inside a conversation**, and a **headless mode**.
- **Undo of what a shell command did.** `/undo` takes back what the file tools changed.
- **Compaction that survives a restart.** The saved conversation is always whole; after `/resume` a long one is summarised again
  at its first request past the trigger.

The tests run the same `Program.RunAsync` against a scripted model, with no key and no network:
`dotnet test --project tests/PinkRooster.Samples.Tests`.
