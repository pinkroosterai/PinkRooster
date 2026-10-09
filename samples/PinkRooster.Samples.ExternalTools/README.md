# PinkRooster.Samples.ExternalTools

Functions from elsewhere, made into a tool collection with its own text, approvals and context.

Any other functions, such as generated ones or another SDK's, go in an `ExternalToolCollection`. It shows `WithInstruction`, `RequireApproval` and `WithContext` on one made from two `AIFunction`s. A run that calls a tool needing approval ends with an approval request; see the Approvals sample for answering one.

## Try it

Run it and ask what the weather in Utrecht is: the model calls `GetWeather`, and the answer uses Celsius (the collection's instruction) and may mention the three online sensors (its context).

`RestartSensor` needs approval, so a run that calls it ends with an approval request and the sample prints `RestartSensor needs approval`. Change the request to "Restart the weather sensor." to see that. This sample does not answer the request; the [Approvals](../PinkRooster.Samples.Approvals/README.md) sample does.

Try it: add a third `AIFunction` to the collection, or remove `RequireApproval` and see `RestartSensor` run at once.

## Run

From the repository root, with a model configured in `samples/PinkRooster.Samples/models.json` (the first run creates it for a local
Ollama; `models.example.json` next to it shows Groq, LM Studio and OpenAI entries):

```text
dotnet run --project samples/PinkRooster.Samples.ExternalTools
```

**Needs:** a model that supports tool calls

**Guide:** [The tool collections guide: functions from elsewhere](../../docs/PinkRooster.ToolCollections.md#functions-from-elsewhere)

The tests run the same `Program.RunAsync` against a scripted model, with no key and no network:
`dotnet test --project tests/PinkRooster.Samples.Tests`.

Previous sample: [SessionState](../PinkRooster.Samples.SessionState/README.md).
