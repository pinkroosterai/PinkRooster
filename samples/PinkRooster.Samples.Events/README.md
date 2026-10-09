# PinkRooster.Samples.Events

Events: watching what an agent does while it runs, the same for a run and a streamed run.

It shows `OnEvent` with a type switch over the events a run publishes (`RunStarted`, `ModelCallCompleted`, `ToolCallStarted`, `ToolCallCompleted`, `RunCompleted`), `OnEvent<TEvent>` that hears one event type without a switch, and that `RunAsync` and `RunStreamingAsync` publish the same events.

## Try it

Run it and watch the event lines. The first run is `RunAsync`, the second `RunStreamingAsync`.

- Expect `run started`, a `model call finished` line, `-> GetTicket` lines for each tool call with a matching `<-` line and its status, the `answer:` line from the typed `OnEvent<AssistantTextCompleted>` handler, and `run Succeeded`.
- The streamed run prints the same event lines, and its answer text also appears piece by piece.

Try it: add a `case` for another event type in the switch, or delete the `OnEvent<AssistantTextCompleted>` line and see only the typed handler's line go.

## Run

From the repository root, with a model configured in `samples/PinkRooster.Samples/models.json` (the first run creates it for a local
Ollama; `models.example.json` next to it shows Groq, LM Studio and OpenAI entries):

```text
dotnet run --project samples/PinkRooster.Samples.Events
```

**Needs:** a model that supports tool calls

**Guide:** [The agent guide: events](../../docs/PinkRooster.Agents.md#events)

The tests run the same `Program.RunAsync` against a scripted model, with no key and no network:
`dotnet test --project tests/PinkRooster.Samples.Tests`.

Previous sample: [Builder](../PinkRooster.Samples.Builder/README.md).
