# PinkRooster.ToolCollections.Mcp

Turns a connected [Model Context Protocol (MCP)](https://modelcontextprotocol.io) server into a [tool collection](https://github.com/pinkroosterai/PinkRooster/blob/main/docs/PinkRooster.ToolCollections.md), so an agent gets the server's tools
with the same instructions, constraints, approvals and context as any other collection.

```text
dotnet add package PinkRooster.ToolCollections.Mcp --prerelease
```

Needs the .NET 10 SDK and an MCP server to connect to; the example below also needs internet access (Context7 works without a key; `CONTEXT7_API_KEY` is optional), and `dotnet add package PinkRooster.Agents --prerelease` to build the agent, with `chatClient` any `IChatClient` ([how to make one](https://github.com/pinkroosterai/PinkRooster/blob/main/docs/PinkRooster.Agents.md#quickstart)). A server started with `npx` needs Node.js. It depends on `PinkRooster.ToolCollections` and `ModelContextProtocol.Core`, and is the only package here that references MCP.
Use it with [`PinkRooster.Agents`](https://github.com/pinkroosterai/PinkRooster/blob/main/docs/PinkRooster.Agents.md) or any Microsoft Agent Framework (MAF) agent.

`McpToolCollection` carries the server's tools and the instructions the server sends for the model. Connect in one line, and keep the collection alive while
the agent runs:

```csharp
using PinkRooster.ToolCollections.Mcp;

await using var docs = await McpToolCollection.ConnectHttpAsync("Context7", new Uri("https://mcp.context7.com/mcp"));
```

<!-- mcp-example:begin -->
```csharp
using PinkRooster.Agents;

var agent = chatClient
    .CreateAgent()
    .WithRole("You answer questions about libraries from their current documentation.")
    .WithTools(docs.WithConstraint("Name the library version you looked up."))
    .Build();
var response = await agent.RunAsync("How do I stream a chat response?");
Console.WriteLine(response.Text);
```
<!-- mcp-example:end -->

- **Connecting.** `ConnectHttpAsync` reaches a remote server. Its `headers` argument leaves out a
  header whose value is null or blank, so an optional key can come straight from the environment:
  `new Dictionary<string, string?> { ["CONTEXT7_API_KEY"] = Environment.GetEnvironmentVariable("CONTEXT7_API_KEY") }`.
  It also takes your own `httpClient`, and `configure` for the rest of the transport options (mode,
  timeout, OAuth).
- **On an agent class.** `[AgentMcpHttpTools("Context7", "https://mcp.context7.com/mcp")]` gives a `DeclaredAgent` the server's tools. The endpoint is a string, because an attribute cannot hold
  a `Uri`. The agent connects on first use, once per instance: the first run awaits the connection and passes its cancellation token to it, and `await agent.InitializeAsync()` connects at start-up instead, so a server that cannot be reached fails there. Either way it connects once, and `await using` or `DisposeAsync` on the agent closes the connection.
  `ApiKeyEnvironmentVariable` (sent as `Bearer <key>` in `HeaderName`, default `Authorization`) and `RequireApprovalForDestructiveTools` cover the common
  cases; for more, call `ConnectHttpAsync` in `Configure`.
  `[AgentMcpStdioTools("Files", "npx", "-y", "@modelcontextprotocol/server-filesystem", ".")]` does the same for a local server
  started as a child process: name, command, then its arguments.
  `ConnectStdioAsync` starts a local server, as in
  `ConnectStdioAsync("Files", "npx", ["-y", "@modelcontextprotocol/server-filesystem", "."])`. The server
  process gets this process's whole environment, your model key included; for a server you do not trust
  pass `inheritEnvironment: false`, and give it what it needs through `environment`. Pass `standardError`
  to see why a server failed to start. And
  `ConnectAsync` takes any `IClientTransport`. Each takes `clientOptions` for what the client offers the
  server (sampling, elicitation, its name) and a `loggerFactory` to see what the client and transport do.
  A connect that fails throws `McpException`, naming the server and what to check; catch it to run
  without that server.
- **Who owns the client.** A collection from a `Connect` method owns its client: disposing the
  collection disconnects it. To share one client you manage yourself, pass it to
  `McpToolCollection.CreateAsync("Name", client)`; that collection leaves the client connected. Either
  way, `docs.Client` reaches the server's resources and prompts.
- **Server instructions** become the collection's first instruction. They are the server's text in
  your agent's system prompt, so pass `includeServerInstructions: false` for a server you do not trust.
- **Approval** applies to the tools you name with `RequireApproval(...)`, and, when you call
  `RequireApprovalForDestructiveTools()`, to every tool the server does not mark read-only or
  non-destructive. The hints can only add approval, never remove it: a server you do not trust can
  mislabel a tool, so name its dangerous tools too.
- **Kind.** A tool the server marks read-only has the [kind](https://github.com/pinkroosterai/PinkRooster/blob/main/docs/PinkRooster.ToolCollections.md#tool-kinds)
  `Read`; every other tool has none, so a host that lets edits run unasked never runs a server's write
  that way. `WithKind(ToolKind.Edit, "write_note")` gives a tool you know a kind yourself. A read-only
  hint is the server's word: for a server you do not trust, take the kind away with
  `WithKind(ToolKind.None, ...)`.
- **Choosing and renaming.** The `select` argument sees each tool and returns it, a renamed copy
  (`tool.WithName("docs_search")`), or null to leave it out. Rename there when two servers share a tool
  name. A kept name that model providers reject, such as `files.read`, or two kept tools with one name,
  throws with the rename to write.
- **The list is fixed.** The tools are the ones the server listed when the collection was created. When
  the server changes them, create a new collection and a new agent.
- **A server that goes away** is not reconnected. A call to its tools can then wait instead of failing,
  so give agent runs a cancellation token with a deadline. Once you dispose a collection from a
  `Connect` method, its tools fail at once, calls still waiting included.

For functions that do not come from an MCP server, see `ExternalToolCollection` in [`PinkRooster.ToolCollections`](https://github.com/pinkroosterai/PinkRooster/blob/main/docs/PinkRooster.ToolCollections.md#functions-from-elsewhere).

**Context from an MCP resource.** A server that keeps state in a resource can feed the collection's
context. Here `gitServer` is an MCP client of your own, already connected:

<!-- mcp-resource-context:begin -->
```csharp
using ModelContextProtocol.Protocol;
using PinkRooster.ToolCollections.Mcp;

var git = await McpToolCollection.CreateAsync("Git", gitServer);
git.WithContext(async ct =>
{
    var branch = await gitServer.ReadResourceAsync("git://current-branch", cancellationToken: ct);
    return $"## Git\nBranch: {branch.Contents.OfType<TextResourceContents>().FirstOrDefault()?.Text}";
});
```
<!-- mcp-resource-context:end -->

That reads the resource before every model call, tool loop included, so each call costs one extra
round trip to the server.

## Feedback and license

Report a bug or ask a question on [GitHub Issues](https://github.com/pinkroosterai/PinkRooster/issues). The package is released under the [MIT license](https://github.com/pinkroosterai/PinkRooster/blob/main/LICENSE).
