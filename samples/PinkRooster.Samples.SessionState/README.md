# PinkRooster.Samples.SessionState

State that belongs to one conversation: a tool collection that keeps notes in the session.

One collection instance serves every session, so a collection that keeps notes per conversation asks for its state with `SessionState(create)`, in a tool method and in `GetContextAsync` alike: the state of the session of the run in progress, kept in the session's `StateBag`. The sample runs two sessions on one agent and shows that each holds its own notes.

## Try it

Run it and read the three results:

1. `The first session holds: ...` shows the tea note only.
2. `The second session holds: ...` shows the coffee note only. One agent and one `NotesTools` instance served both.
3. The answer to "What do you know about my taste?" mentions tea and not coffee, because the notes come back through `GetContextAsync` from that session's `StateBag`.

Try it: ask the same question on the `coffee` session; change `Current()` to use a field and see both sessions mix.

## Run

From the repository root, with a model configured in `samples/PinkRooster.Samples/models.json` (the first run creates it for a local
Ollama; `models.example.json` next to it shows Groq, LM Studio and OpenAI entries):

```text
dotnet run --project samples/PinkRooster.Samples.SessionState
```

**Needs:** a model that supports tool calls

**Guide:** [The tool collections guide: state that belongs to one session](../../docs/PinkRooster.ToolCollections.md#state-that-belongs-to-one-session)

The tests run the same `Program.RunAsync` against a scripted model, with no key and no network:
`dotnet test --project tests/PinkRooster.Samples.Tests`.

Previous sample: [ToolCollections](../PinkRooster.Samples.ToolCollections/README.md).
