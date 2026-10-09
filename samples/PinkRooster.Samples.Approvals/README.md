# PinkRooster.Samples.Approvals

Approvals: a run that ends until a person says yes, and continues on the same session.

It shows both ways to mark a tool: `[Tool(..., RequiresApproval = true)]` on `TicketTools.CloseTicket`, and `RequireApproval("ArchiveTicket")` on the builder for a tool that has no attribute. A run that calls such a tool ends with a `ToolApprovalRequestContent`; the sample asks you, then sends the answer back on the same session. Answer "Skip" to see what the model is told.

## Try it

Run it and the model is asked to close PR-8 and then archive it. Both tools need your approval, so the run stops and the terminal asks you about the call.

- Answer **Yes** and the run continues on the same session; the closing line reports what was done.
- Answer **Skip** and the model is told "The user did not allow this.", so you see how it explains a refused tool.

Try it: remove `RequireApproval("ArchiveTicket")` and the archive step runs without a question; set `RequiresApproval = false` on `CloseTicket` in `PinkRooster.Samples/Tickets/TicketTools.cs` and that one does too.

## Run

From the repository root, with a model configured in `samples/PinkRooster.Samples/models.json` (the first run creates it for a local
Ollama; `models.example.json` next to it shows Groq, LM Studio and OpenAI entries):

```text
dotnet run --project samples/PinkRooster.Samples.Approvals
```

**Needs:** an interactive terminal and a model that supports tool calls

**Guide:** [The agent guide: approvals](../../docs/PinkRooster.Agents.md#approvals)

The tests run the same `Program.RunAsync` against a scripted model, with no key and no network:
`dotnet test --project tests/PinkRooster.Samples.Tests`.

Previous sample: [Events](../PinkRooster.Samples.Events/README.md).
