namespace PinkRooster.ToolCollections.Mcp;

/// <summary>Gives an agent class the tools of a local MCP server started as a child process and reached over its standard input and output, connected when the agent is first used.</summary>
/// <remarks>
/// <para>
/// Each agent instance starts its own server process and stops it when the agent is disposed. For environment variables, tool
/// selection or the server's error output, call <see cref="McpToolCollection.ConnectStdioAsync"/> and add the result with
/// <c>WithTools</c> in <c>Configure</c>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [AgentRole("You tidy the files of this project.")]
/// [AgentMcpStdioTools("Files", "npx", "-y", "@modelcontextprotocol/server-filesystem", ".", RequireApprovalForDestructiveTools = true)]
/// public sealed class FilesAgent(IChatClient chatClient) : DeclaredAgent(chatClient);
/// </code>
/// </example>
public sealed class AgentMcpStdioToolsAttribute : ToolCollectionSourceAttribute
{
    /// <summary>How messages name the collection, such as the server's name.</summary>
    public string Name { get; }

    /// <summary>The program that runs the server, such as <c>npx</c> or a path.</summary>
    public string Command { get; }

    /// <summary>The program's arguments, in order.</summary>
    public string[] Arguments { get; }

    /// <summary>The folder the server process starts in; this process's current folder when null.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Whether tools the server marks as destructive need the host's approval; see <see cref="McpToolCollection.RequireApprovalForDestructiveTools"/>.</summary>
    public bool RequireApprovalForDestructiveTools { get; init; }

    /// <param name="name">How messages name the collection, such as the server's name.</param>
    /// <param name="command">The program that runs the server, such as <c>npx</c>.</param>
    /// <param name="arguments">The program's arguments, in order.</param>
    public AgentMcpStdioToolsAttribute(string name, string command, params string[] arguments)
    {
        Name = name;
        Command = command;
        Arguments = arguments ?? [];
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The name or the command is blank; the message says what to write.</exception>
    public override async ValueTask<ToolCollection> CreateAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidOperationException("[AgentMcpStdioTools] has a blank name; pass the server's name as the first argument, such as \"Files\".");
        }
        if (string.IsNullOrWhiteSpace(Command))
        {
            throw new InvalidOperationException($"[AgentMcpStdioTools] for '{Name}' has a blank command; pass the program that runs the server as the second argument, such as \"npx\".");
        }

        McpToolCollection collection = await McpToolCollection.ConnectStdioAsync(Name, Command, Arguments, workingDirectory: WorkingDirectory, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (RequireApprovalForDestructiveTools)
        {
            collection.RequireApprovalForDestructiveTools();
        }
        return collection;
    }
}
