# PinkRooster.Samples.SteppedAgents

An agent that takes several turns in one run, with steps of its own.

It shows `SteppedAgent`: `MaxTurns` caps the turns of one message, `StartAsync` keeps the original request in the session with `SetState`, and `NextAsync` runs after each turn and returns `NextStep.Stop()` or `NextStep.Send(label, prompt)`. This one translates, then reads its own translation once more against the original. `StepStarted` events name each step.

## Try it

Run it and read the output:

1. `step: ...` lines name each step as it starts: the first turn, then `revise`.
2. The translation printed is the reply of the last turn: the agent translated the Dutch sentence, then read its translation against the original and fixed any mistake.

Try it: change `MaxTurns` to `1` and `revise` never runs; make `NextAsync` return `NextStep.Stop()` to end after one turn; send another sentence.

## Run

From the repository root, with a model configured in `samples/PinkRooster.Samples/models.json` (the first run creates it for a local
Ollama; `models.example.json` next to it shows Groq, LM Studio and OpenAI entries):

```text
dotnet run --project samples/PinkRooster.Samples.SteppedAgents
```

**Needs:** any chat model; no tool calls

**Guide:** [The agent guide: agent classes](../../docs/PinkRooster.Agents.md#agent-classes)

The tests run the same `Program.RunAsync` against a scripted model, with no key and no network:
`dotnet test --project tests/PinkRooster.Samples.Tests`.

Previous sample: [AgentClasses](../PinkRooster.Samples.AgentClasses/README.md).
