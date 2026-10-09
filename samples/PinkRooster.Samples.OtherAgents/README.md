# PinkRooster.Samples.OtherAgents

Tool collections on a MAF agent the builder did not make.

Give collections to any MAF agent through `ToolCollectionContextProvider`, which sends the tools, their standing text and their context once per run. For context on every model call of a hand-built `ChatClientAgent`, put the collections in its client with `chatClient.AsBuilder().UseToolCollections(...)` and pass their tools yourself. Use one of the two per agent, not both, or the context arrives twice.

## Try it

Run it and you get two answers, one for each way of giving the same `TicketTools` to a `ChatClientAgent` built by hand:

1. **Through the context provider**: `ToolCollectionContextProvider` sends the tools, their standing text and their context once per run.
2. **Through the client**: `UseToolCollections` sends the context on every model call, tool loop included, and you pass the tools yourself.

Both should answer from the ticket store. Use one way per agent, not both, or the context arrives twice.

Try it: put `ToolCollectionContextProvider` on the second agent too and print the messages (see the `MafPassthrough` sample for a client middleware that does) to see the duplicate.

## Run

From the repository root, with a model configured in `samples/PinkRooster.Samples/models.json` (the first run creates it for a local
Ollama; `models.example.json` next to it shows Groq, LM Studio and OpenAI entries):

```text
dotnet run --project samples/PinkRooster.Samples.OtherAgents
```

**Needs:** a model that supports tool calls

**Guide:** [The tool collections guide: agents that were not built with PinkRooster.Agents](../../docs/PinkRooster.ToolCollections.md#agents-that-were-not-built-with-pinkroosteragents)

The tests run the same `Program.RunAsync` against a scripted model, with no key and no network:
`dotnet test --project tests/PinkRooster.Samples.Tests`.

Previous sample: [ExternalTools](../PinkRooster.Samples.ExternalTools/README.md).
