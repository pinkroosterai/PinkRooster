# PinkRooster.Samples.Mcp

Tools from MCP servers, as tool collections with their own instructions and approvals.

It shows `McpToolCollection.ConnectHttpAsync` (Context7, which looks up current library documentation; `CONTEXT7_API_KEY` is optional and raises the rate limit) and `ConnectStdioAsync` (a local filesystem server started with `npx`), `select` to rename tools so two servers cannot clash, `RequireApprovalForDestructiveTools()` so every tool a server does not mark read-only asks first, `WithConstraint`, and `WithContext` reading a server resource. A server that cannot be reached is reported and skipped; the rest still runs. The collections own their clients, so they are disposed when the run ends.

## Try it

This sample reaches two real MCP servers, so it needs network access and `npx` (Node.js) for the second one.

Run it and the model is asked to look up how to stream a chat response with Microsoft.Extensions.AI, then to list the project's markdown files.

- `-> ...` lines show each tool call. The Context7 tools are read-only and run at once; the file server's tools are named `files_...`.
- Tools of the file server that are not marked read-only ask you first (`RequireApprovalForDestructiveTools`). Approve or refuse each.
- If a server cannot be reached, the sample says so and carries on with the other. With neither, it stops with a message.
- The server has no `project://summary` resource by default, so you see one note that its context stays empty.

Try it: set `CONTEXT7_API_KEY` for a higher rate limit; change the request to ask for a file to be written, to see an approval for a write tool.

## Run

From the repository root, with a model configured in `samples/PinkRooster.Samples/models.json` (the first run creates it for a local
Ollama; `models.example.json` next to it shows Groq, LM Studio and OpenAI entries):

```text
dotnet run --project samples/PinkRooster.Samples.Mcp
```

**Needs:** an interactive terminal and a model that supports tool calls; Context7 needs network access, and the file server needs `npx` (Node.js); each is skipped when it is missing

**Guide:** [The MCP guide](../../docs/PinkRooster.ToolCollections.Mcp.md)

The tests run the same `Program.RunAsync` against a scripted model, with no key and no network:
`dotnet test --project tests/PinkRooster.Samples.Tests`.

Previous sample: [OtherAgents](../PinkRooster.Samples.OtherAgents/README.md).
