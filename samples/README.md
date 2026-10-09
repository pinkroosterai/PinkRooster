# Samples

One console project for each feature of the Agents family: `PinkRooster.Agents`,
`PinkRooster.ToolCollections` and the packages built on it. Each shows one feature in one `Program.cs`; the setup every sample shares
(which model to talk to, how the terminal draws events and asks questions) is in [`PinkRooster.Samples`](PinkRooster.Samples/README.md).
Set up a model there once, then run any sample with `dotnet run --project samples/<name>`.

The suggested order is the table's order; each README names the one before it.

| Sample | Shows |
|---|---|
| [`Builder`](PinkRooster.Samples.Builder/README.md) | The sections of the system prompt, `Apply`, `Clone`, `BuildOptions` |
| [`Events`](PinkRooster.Samples.Events/README.md) | `OnEvent`, `OnEvent<T>`, a run and a streamed run |
| [`Approvals`](PinkRooster.Samples.Approvals/README.md) | `RequiresApproval`, `RequireApproval`, the session loop |
| [`Middleware`](PinkRooster.Samples.Middleware/README.md) | `Use`, `AgentEvents.Publish`, an event of your own |
| [`MafPassthrough`](PinkRooster.Samples.MafPassthrough/README.md) | History, context, client pipeline, tool loop and options of MAF |
| [`AgentClasses`](PinkRooster.Samples.AgentClasses/README.md) | `DeclaredAgent`: attributes, own tools, `Configure`, own context, events |
| [`SteppedAgents`](PinkRooster.Samples.SteppedAgents/README.md) | `SteppedAgent`: `MaxTurns`, `StartAsync`, `NextAsync`, step state |
| [`ToolCollections`](PinkRooster.Samples.ToolCollections/README.md) | Your own collection: tools, standing text, current state |
| [`SessionState`](PinkRooster.Samples.SessionState/README.md) | State per conversation in the session's `StateBag` |
| [`ExternalTools`](PinkRooster.Samples.ExternalTools/README.md) | `ExternalToolCollection` from plain functions |
| [`OtherAgents`](PinkRooster.Samples.OtherAgents/README.md) | Collections on a MAF agent the builder did not make |
| [`Mcp`](PinkRooster.Samples.Mcp/README.md) | `McpToolCollection` over HTTP and stdio, `select`, approvals, resource context |
| [`Reasoning`](PinkRooster.Samples.Reasoning/README.md) | `ReasoningFieldChatClient`, reasoning options and events |
| [`BackgroundTools`](PinkRooster.Samples.BackgroundTools/README.md) | `AllowBackground`, `ConfigureBackground`, the task tools, a run that waits for its tasks |
| [`Imaging`](PinkRooster.Samples.Imaging/README.md) | `ImageToolCollection`: `QueryImage` on a vision model, limits, counting the vision calls |

## Showcase

[`CodingAgent`](PinkRooster.Samples.CodingAgent/README.md) is the one sample that shows many features at once: a small terminal
coding assistant with permission modes, a verify step, helpers, background tasks, skills, MCP servers, saved conversations, undo
and compaction. It is a project of its own, without the shared setup above, and its README maps each thing it does to the library
feature behind it and to that feature's sample in the table.

Every sample's tests run its `Program.RunAsync` against a scripted model, with no key and no network:
`dotnet test --project tests/PinkRooster.Samples.Tests`.
