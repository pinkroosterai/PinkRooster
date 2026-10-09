# PinkRooster.Samples.MafPassthrough

What MAF offers, reached through the builder: history, context, client pipeline, tool loop and options.

It shows `WithChatHistoryProvider`, `WithContextProvider` (any MAF `AIContextProvider`), `ConfigureClient` (middleware on the chat client, here one line per model call), `ConfigureToolLoop`, `ConfigureChatOptions` and `ConfigureAgentOptions`. None of them is wrapped or renamed: the builder hands the objects to MAF.

## Try it

Run it and read the output:

1. Each model call prints `model call with N messages` from the `ConfigureClient` middleware. A tool call makes a second model call, so you see the count grow within one question.
2. The first answer is about PR-7. The second question, "And is the shop open now?", is answered from the line `ShopHoursProvider` adds to the instructions, and the history provider keeps the first exchange, so the message count is larger.
3. The last line prints the description set through `ConfigureAgentOptions`.

Try it: change `MaximumIterationsPerRequest` to `1` and watch the tool loop stop early; change the temperature.

## Run

From the repository root, with a model configured in `samples/PinkRooster.Samples/models.json` (the first run creates it for a local
Ollama; `models.example.json` next to it shows Groq, LM Studio and OpenAI entries):

```text
dotnet run --project samples/PinkRooster.Samples.MafPassthrough
```

**Needs:** a model that supports tool calls

**Guide:** [The agent guide: everything else MAF offers](../../docs/PinkRooster.Agents.md#everything-else-maf-offers)

The tests run the same `Program.RunAsync` against a scripted model, with no key and no network:
`dotnet test --project tests/PinkRooster.Samples.Tests`.

Previous sample: [Middleware](../PinkRooster.Samples.Middleware/README.md).
