# Changelog

## 0.4.0-preview.1

The first public release. Six packages, released together at one version; each depends on its siblings at exactly this version.
The libraries are pre-release: a public API may change or go between previews, and this file will say so.

**`PinkRooster.ToolCollections`**: the `ToolCollection` base class. One class holds a group of `[Tool]` methods, the standing instructions and
constraints that go with them, and `GetContextAsync`, a note on the tools' current state that is sent before every model call without entering
the chat history.

- Approval per tool (`RequiresApproval`) and a kind per tool (`ToolKind`: `Read`, `State`, `Edit`, `Execute`), read from any `AITool` with `tool.GetKind()`.
- State per session (`SessionState`, `SetSessionState`), kept in the session's `StateBag` and so saved and restored with it.
- `ExternalToolCollection` gives plain `AIFunction`s from elsewhere the same instructions, constraints, approvals, kinds and context.
- `ToolCollectionContextProvider` gives collections to any MAF agent, including one this project did not build.
- A `[Tool]` on a method that is not public fails when the agent is built, naming the method and the fix.

**`PinkRooster.Agents`**: `AgentBuilder`, a fluent way to compose an agent whose `Build()` returns a normal MAF `AIAgent`. Depends on
`Microsoft.Agents.AI` at `[1.24.0, 1.25.0)`.

- The system prompt in named sections (role, objective, background, instructions, constraints, output format, examples), with checks that fail early and name the fix.
- Agent classes: `DeclaredAgent` (attributes, own `[Tool]` methods, `Configure`, own context) and `SteppedAgent` (a step loop with `MaxTurns`, `StartAsync` and `NextAsync`).
- Events: one typed stream of reasoning, answer text, tool calls, approvals, steps and background tasks, through `OnEvent`.
- Approvals: `RunWithApprovalsAsync` and `RunStreamingWithApprovalsAsync` run the whole approval loop; `PermissionPolicy` answers from a mode (`Ask`, `AcceptEdits`, `Plan`) and allow rules, and asks the user only about the rest.
- Sub-agents: `SubAgentToolCollection` lets a model hand work to other agents, built from the models and tool sets you name.
- Background tools: `AllowBackground` lets the model start a slow tool call, go on, and read the result when it is ready; `PostMessageAsync` posts a message to a running agent's inbox.
- `SessionStore` saves and restores sessions as files, and names the tools that are gone or new when one is restored.
- Middleware (`Use`), services (`WithServices`) and everything else MAF offers stay reachable from the builder.

**`PinkRooster.ToolCollections.BuiltIn`**: ready-made collections, with no model-provider or UI dependency.

- `ShellToolCollection`: runs commands with a timeout, output limits and allow and deny prefix lists. The lists guard against mistakes; they are not a sandbox.
- `FileReadToolCollection` and `FileOperationsToolCollection`: read, search and edit files inside a `Workspace`; `KeepBackups()` records every change per session so `UndoAsync` can take a user message's changes back.
- `AskUserQuestionToolCollection`: multiple-choice questions to the user.
- `TaskListToolCollection`: a task list with dependencies and pluggable storage, one list per session with `PerSession()`.
- `DateTimeToolCollection`: the current date and time.
- `ImageToolCollection`: `QueryImage` sends image files and a question to a model that accepts images and returns the answer as text, so an agent whose own model cannot see can still work with screenshots.

**`PinkRooster.ToolCollections.Mcp`**: `McpToolCollection` turns an MCP server, over HTTP or stdio, into a tool collection: the server's tools and
instructions, with selection, renaming, approvals and context. `[AgentMcpHttpTools]` and `[AgentMcpStdioTools]` give one to an agent class.

**`PinkRooster.OpenAI.Reasoning`**: `ReasoningFieldChatClient` reads the reasoning field that Ollama, Groq and other OpenAI-compatible servers
send and the Microsoft.Extensions.AI OpenAI adapter drops, and returns it as reasoning content.

**`PinkRooster.SpectreConsole`**: `AgentConsole` shows an agent's run on a Spectre.Console terminal.

- Draws reasoning, steps, tool calls with their timing, sub-agent runs and background tasks, and the answer as markdown while the model writes it.
- Asks the user to approve a tool call (skip, allow, allow for the session), to answer an `AskUserQuestion` question, or to type a line.
- `RunToConsoleAsync` runs an agent and answers its approvals through the console; `RunWithInputAsync` also keeps an input line open while the agent runs.
- Redacts the sensitive values you name, also when one is split across pieces of streamed text.
- `IAnsiConsole.WriteAnswer(string)` draws a whole markdown answer without an `AgentConsole`.
