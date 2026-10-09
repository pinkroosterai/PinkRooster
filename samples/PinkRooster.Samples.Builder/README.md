# PinkRooster.Samples.Builder

The agent builder: the sections of the system prompt, one call each.

It shows `CreateAgent()` with `WithRole`, `WithObjective`, `WithBackground`, `WithInstruction`, `WithConstraint`, `WithOutputFormat` and `WithExample`, shared setup with `Apply`, `Clone` for a variant of one base, and `BuildOptions()` to see the system prompt the model will get before any model call. `WithoutDefaults()` and `WithSystemPrompt(...)` are the two ways to change what the prompt starts from.

## Try it

Nothing to answer here; read the output in three parts:

1. **The system prompt** is printed before any model call, from `BuildOptions()`. Find each section there (role, objective, background, instructions, constraints, output format, example) and the line `HouseRules` added through `Apply`. The order is fixed, whatever the order of the builder calls.
2. The review from the `strict` clone follows. It has the base brief plus one more constraint; the base `reviewer` is untouched.
3. The last line compares the length of a prompt built with `WithoutDefaults()` and one written whole with `WithSystemPrompt(...)`.

Try it: move the `With...` calls around and see the printed prompt not change; add a constraint to the clone only.

## Run

From the repository root, with a model configured in `samples/PinkRooster.Samples/models.json` (the first run creates it for a local
Ollama; `models.example.json` next to it shows Groq, LM Studio and OpenAI entries):

```text
dotnet run --project samples/PinkRooster.Samples.Builder
```

**Needs:** any chat model; no tool calls

**Guide:** [The agent guide: composing an agent](../../docs/PinkRooster.Agents.md#composing-an-agent)

The tests run the same `Program.RunAsync` against a scripted model, with no key and no network:
`dotnet test --project tests/PinkRooster.Samples.Tests`.

This is the first sample; start here.
