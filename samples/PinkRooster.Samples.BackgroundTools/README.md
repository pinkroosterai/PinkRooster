# PinkRooster.Samples.BackgroundTools

Background tools: the model starts slow tool calls, goes on, and reads each result when it is ready.

`AllowBackground("CountTickets")` gives that tool one more optional parameter, `runInBackground`. A call with it returns a task id at once. The agent also gets four tools to follow its tasks: `WaitForTasks`, which returns the results of the tasks that have ended, `GetTaskResult`, `ListTasks` and `CancelTask`. `ConfigureBackground` sets the limits. The run stays alive while a task runs: when the model ends its turn early, it is called again as soon as a task ends.

## Try it

Run it and the model is asked for three counts that each take a random 4 to 16 seconds. The terminal shows a line for each task that starts and each that ends, so you can see the three overlap and end in no fixed order.

- With the three calls in the background the counting takes as long as the slowest count, not the three added up.
- A model that leaves `runInBackground` out still gets the right numbers; the calls then run one after another.

Try it: set `MaxRunningTasks = 2` and the third start is refused with an error the model reads and reacts to; remove `AllowBackground` and the parameter and the four task tools are gone.

## Run

From the repository root, with a model configured in `samples/PinkRooster.Samples/models.json` (the first run creates it for a local
Ollama; `models.example.json` next to it shows Groq, LM Studio and OpenAI entries):

```text
dotnet run --project samples/PinkRooster.Samples.BackgroundTools
```

**Needs:** a model that supports tool calls

**Guide:** [The agent guide: background tools](../../docs/PinkRooster.Agents.md#background-tools)

The tests run the same `Program.RunAsync` against a scripted model, with no key and no network:
`dotnet test --project tests/PinkRooster.Samples.Tests`.

Previous sample: [Reasoning](../PinkRooster.Samples.Reasoning/README.md).
