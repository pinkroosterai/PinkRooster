# PinkRooster.Samples.Middleware

Middleware: wrapping the agent with your own code, and publishing your own events.

It shows `Use((inner, services) => ...)`, MAF's agent middleware: an agent that wraps the one the builder made, sits inside the event layer, and publishes a custom event with `AgentEvents.Publish`. Here it escalates a reply that promises a refund; the handler added with `OnEvent` hears it like any built-in event.

## Try it

Run it: the customer asks for their money back. If the model's reply mentions a refund, the line `Escalated to a person: The reply promises a refund.` prints before the reply. The line comes from `EscalationAgent`, which publishes an `Escalated` event that the `OnEvent` handler hears like a built-in one.

A model that does not say "refund" prints no escalation line; ask again, or change the request to make it likelier.

Try it: change the word `EscalationAgent` looks for, or add a second `Use(...)` and see which wraps which (the first call sits nearest the agent).

## Run

From the repository root, with a model configured in `samples/PinkRooster.Samples/models.json` (the first run creates it for a local
Ollama; `models.example.json` next to it shows Groq, LM Studio and OpenAI entries):

```text
dotnet run --project samples/PinkRooster.Samples.Middleware
```

**Needs:** any chat model; no tool calls

**Guide:** [The agent guide: your own middleware](../../docs/PinkRooster.Agents.md#your-own-middleware)

The tests run the same `Program.RunAsync` against a scripted model, with no key and no network:
`dotnet test --project tests/PinkRooster.Samples.Tests`.

Previous sample: [Approvals](../PinkRooster.Samples.Approvals/README.md).
