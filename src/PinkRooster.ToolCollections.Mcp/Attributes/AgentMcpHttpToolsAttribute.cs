
namespace PinkRooster.ToolCollections.Mcp;

/// <summary>Gives an agent class the tools of a remote MCP server reached over HTTP, connected when the agent is first used.</summary>
/// <remarks>
/// <para>
/// Each agent instance opens its own connection and closes it when the agent is disposed. The endpoint is a string because an attribute
/// cannot hold a <see cref="Uri"/>. For headers beyond one API key, tool selection or OAuth, call
/// <see cref="McpToolCollection.ConnectHttpAsync"/> and add the result with <c>WithTools</c> in <c>Configure</c>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [AgentRole("You answer questions from online sources.")]
/// [AgentMcpHttpTools("Context7", "https://mcp.context7.com/mcp", ApiKeyEnvironmentVariable = "CONTEXT7_API_KEY")]
/// public sealed class ResearchAgent(IChatClient chatClient) : DeclaredAgent(chatClient);
/// </code>
/// </example>
public sealed class AgentMcpHttpToolsAttribute : ToolCollectionSourceAttribute
{
    /// <summary>How messages name the collection, such as the server's name.</summary>
    public string Name { get; }

    /// <summary>The server's URL.</summary>
    public string Endpoint { get; }

    /// <summary>
    /// The environment variable that holds an API key, sent as <c>Bearer &lt;key&gt;</c> in <see cref="HeaderName"/>. When the variable is
    /// unset or blank no header is sent.
    /// </summary>
    public string? ApiKeyEnvironmentVariable { get; init; }

    /// <summary>The header that carries the key; <c>Authorization</c> by default.</summary>
    public string HeaderName { get; init; } = "Authorization";

    /// <summary>Whether tools the server marks as destructive need the host's approval; see <see cref="McpToolCollection.RequireApprovalForDestructiveTools"/>.</summary>
    public bool RequireApprovalForDestructiveTools { get; init; }

    /// <param name="name">How messages name the collection, such as the server's name.</param>
    /// <param name="endpoint">The server's URL, such as <c>https://mcp.context7.com/mcp</c>.</param>
    public AgentMcpHttpToolsAttribute(string name, string endpoint)
    {
        Name = name;
        Endpoint = endpoint;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The name is blank or the endpoint is not an absolute http or https URL; the message says what to write.</exception>
    public override async ValueTask<ToolCollection> CreateAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidOperationException("[AgentMcpHttpTools] has a blank name; pass the server's name as the first argument, such as \"Context7\".");
        }
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out Uri? endpoint) || endpoint.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException($"[AgentMcpHttpTools] for '{Name}' has the endpoint '{Endpoint}', which is not an absolute http or https URL; write it like \"https://mcp.example.com/mcp\".");
        }

        Dictionary<string, string?>? headers = null;
        if (!string.IsNullOrWhiteSpace(ApiKeyEnvironmentVariable))
        {
            string? key = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
            headers = new() { [HeaderName] = string.IsNullOrWhiteSpace(key) ? null : $"Bearer {key}" };
        }

        McpToolCollection collection = await McpToolCollection.ConnectHttpAsync(Name, endpoint, headers, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (RequireApprovalForDestructiveTools)
        {
            collection.RequireApprovalForDestructiveTools();
        }
        return collection;
    }
}
