# PinkRooster.Samples.ToolCollections

A tool collection of your own: tools, standing text, and a note on the current state.

It shows a `ToolCollection` class with `[Tool]` methods, `[ToolCollectionInstruction]` and `[ToolCollectionConstraint]` for fixed lines, `AddInstruction` from the constructor for a line that depends on how the collection was made, and `GetContextAsync`, which the agent sends before every model call and never stores in the session's history. The sample prints the system prompt first, so you see what the collection added.

## Try it

Run it and read the output:

1. **The system prompt**, printed before any model call. Find the collection's lines: the instruction and constraint from the attributes and `Totals are in euro.` added from the constructor.
2. The answer about order A-1001, from the `GetOrder` tool. The open-order count from `GetContextAsync` is sent with each model call and is not kept in the history.

Try it: change `currency: "euro"` and see the printed prompt change; ask "Which orders are open?" to call `ListOpenOrders`.

## Run

From the repository root, with a model configured in `samples/PinkRooster.Samples/models.json` (the first run creates it for a local
Ollama; `models.example.json` next to it shows Groq, LM Studio and OpenAI entries):

```text
dotnet run --project samples/PinkRooster.Samples.ToolCollections
```

**Needs:** a model that supports tool calls

**Guide:** [The tool collections guide](../../docs/PinkRooster.ToolCollections.md#writing-a-tool-collection)

The tests run the same `Program.RunAsync` against a scripted model, with no key and no network:
`dotnet test --project tests/PinkRooster.Samples.Tests`.

Previous sample: [SteppedAgents](../PinkRooster.Samples.SteppedAgents/README.md).
