# PinkRooster.Samples.Reasoning

Reasoning: showing what a reasoning model thinks, from the servers that send it in a field the OpenAI adapter drops.

It shows `ReasoningFieldChatClient` from `PinkRooster.OpenAI.Reasoning`, which adds the `reasoning` field that Ollama and Groq send back as `TextReasoningContent` (Microsoft.Extensions.AI.OpenAI reads only `reasoning_content`), `ReasoningOptions` with `Output = ReasoningOutput.Full` so the reasoning is requested in full, and the `ReasoningDelta` and `ReasoningCompleted` events. The effort comes from the `reasoningEffort` of the model in `models.json`. A model that sends no reasoning shows none.

## Try it

This needs a model that sends its reasoning back (an Ollama or Groq reasoning model, for example); a model that sends none shows none.

Run it and the puzzle (the bat and the ball) prints `Reasoning: ...` with what the model thought, then `Answer: ...`. The correct answer is 5 cents.

Try it: set `reasoningEffort` on the model in `models.json` and compare how much it thinks; wrap the client without `ReasoningFieldChatClient` and see the `Reasoning:` line disappear.

## Run

From the repository root, with a model configured in `samples/PinkRooster.Samples/models.json` (the first run creates it for a local
Ollama; `models.example.json` next to it shows Groq, LM Studio and OpenAI entries):

```text
dotnet run --project samples/PinkRooster.Samples.Reasoning
```

**Needs:** any chat model; no tool calls

**Guide:** [The reasoning guide](../../docs/PinkRooster.OpenAI.Reasoning.md)

The tests run the same `Program.RunAsync` against a scripted model, with no key and no network:
`dotnet test --project tests/PinkRooster.Samples.Tests`.

Previous sample: [Mcp](../PinkRooster.Samples.Mcp/README.md).
