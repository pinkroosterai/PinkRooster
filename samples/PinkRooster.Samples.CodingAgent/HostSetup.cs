using Microsoft.Extensions.AI;
using PinkRooster.Samples.CodingAgent.Mcp;
using PinkRooster.Samples.CodingAgent.ModelSettings;
using PinkRooster.Samples.CodingAgent.Project;
using PinkRooster.SpectreConsole;
using PinkRooster.ToolCollections.Mcp;

namespace PinkRooster.Samples.CodingAgent;

/// <summary>Everything <see cref="Program.RunAsync"/> takes from outside, so a test runs the same program on a scripted model and a recording console.</summary>
public sealed record HostSetup
{
    /// <summary>The directory the assistant works in.</summary>
    public required string WorkspaceRoot { get; init; }

    /// <summary>The model settings file.</summary>
    public required string ModelSettingsPath { get; init; }

    /// <summary>Makes the screen, given the secrets it must never draw: every API key of the model settings.</summary>
    public required Func<IReadOnlyCollection<string>, IAgentConsole> CreateConsole { get; init; }

    /// <summary>Makes the client of one model.</summary>
    public Func<ModelEntry, IChatClient> CreateClient { get; init; } = ModelClient.Create;

    /// <summary>The MCP servers to use when the workspace's config file names none. <c>Main</c> gives Context7; a test gives its own.</summary>
    public IReadOnlyList<McpServer> DefaultMcpServers { get; init; } = [];

    /// <summary>Connects one MCP server. A test replaces it with a server that runs in the test process.</summary>
    public Func<McpServer, CancellationToken, Task<McpToolCollection>> ConnectMcpAsync { get; init; } = McpServers.ConnectAsync;

    /// <summary>Ctrl+C: stops the run in progress, or ends the program at the prompt.</summary>
    public Interrupt Interrupt { get; init; } = new();
}
