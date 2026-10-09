# PinkRooster.Samples.AgentClasses

An agent written as a class: the brief as attributes, tools of its own, its own context, and its own events.

It shows `DeclaredAgent` with `[AgentRole]`, `[AgentObjective]`, `[AgentInstruction]`, `[AgentConstraint]` and `[AgentOutputFormat]`, a public `[Tool]` method on the class, `Configure` for what depends on a constructor argument, `GetContextAsync` for the class's own context, an `OnEvent` override, and a handler a host adds on the instance and removes again.

## Try it

Run it and read the output top to bottom:

1. Lines like `CountLines: Succeeded` come from the handler the host adds on the instance with `OnEvent<ToolCallCompleted>`. The instructions tell the model to call `CountLines` first, so expect at least one.
2. The review itself follows, as a numbered list (`[AgentOutputFormat]`) that quotes lines (`[AgentInstruction]`).
3. The last line lists the tool calls the class saw through its own `OnEvent` override.

Try it: change the repository name passed to `ReviewerAgent` and see it in the context and the instruction; delete the `using` handler line and the first lines disappear while the last one stays; add a second `[Tool]` method and watch the model use it.

## Run

From the repository root, with a model configured in `samples/PinkRooster.Samples/models.json` (the first run creates it for a local
Ollama; `models.example.json` next to it shows Groq, LM Studio and OpenAI entries):

```text
dotnet run --project samples/PinkRooster.Samples.AgentClasses
```

**Needs:** a model that supports tool calls

**Guide:** [The agent guide: agent classes](../../docs/PinkRooster.Agents.md#agent-classes)

The tests run the same `Program.RunAsync` against a scripted model, with no key and no network:
`dotnet test --project tests/PinkRooster.Samples.Tests`.

Previous sample: [MafPassthrough](../PinkRooster.Samples.MafPassthrough/README.md).
