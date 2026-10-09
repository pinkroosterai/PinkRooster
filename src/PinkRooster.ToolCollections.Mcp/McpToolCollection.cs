using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Client;

namespace PinkRooster.ToolCollections.Mcp;

/// <summary>
/// A tool collection made from a connected MCP server: its tools, and the instructions the server sends for the model, with room
/// for your own instructions, constraints, approvals and live context on top.
/// </summary>
/// <remarks>
/// <para>
/// The tools are the ones the server listed when the collection was created; a later change on the server reaches only
/// collections created after it.
/// </para>
/// <para>
/// Once a collection that owns its client is disposed, a call of its tools fails at once with
/// <see cref="ObjectDisposedException"/>, and a call still waiting for the server is stopped the same way. When the server goes
/// away on its own (the connection drops, or its process exits), a call may fail or may wait: over the SDK's stream transport it
/// waits until the agent's run is cancelled, since nothing here times a call out, so give runs a cancellation token with a
/// deadline. A failed call reaches the model as a failed tool result. The collection does not reconnect: dispose it, connect a
/// new one and build a new agent.
/// </para>
/// <para>
/// A collection from a <c>Connect</c> method owns its client: keep the collection while agents use the tools, and dispose it to
/// disconnect. A collection from <see cref="CreateAsync"/> does not own the client you pass: keep that client connected while
/// agents use the tools, and dispose it yourself.
/// </para>
/// <para>
/// Approval comes from <see cref="ExternalToolCollection{TSelf}.RequireApproval"/>, and from <see cref="RequireApprovalForDestructiveTools"/> when you call it.
/// Nothing else reads the read-only and destructive hints a server puts on its tools, because MCP says not to trust them to make
/// a call less guarded.
/// </para>
/// <para>
/// Server text reaches the model in two places: the server's instructions, which <c>includeServerInstructions: false</c> leaves
/// out, and each tool's description, which is always sent. Leaving the instructions out does not by itself make a server you do
/// not trust safe.
/// </para>
/// </remarks>
public sealed class McpToolCollection : ExternalToolCollection<McpToolCollection>, IAsyncDisposable
{
    // OpenAI's rule for function names, the strictest of the common providers.
    private const int MaxToolNameLength = 64;

    private readonly IReadOnlyList<McpClientTool> tools;
    private readonly McpClient client;
    private readonly bool ownsClient;

    // Cancelled when an owning collection is disposed, which stops its tools' calls; never disposed itself, because a tool an
    // agent still holds may read its token after that.
    private readonly CancellationTokenSource disposal;

    private McpToolCollection(string name, IReadOnlyList<McpClientTool> tools, McpClient client, bool ownsClient)
        : this(name, tools, client, ownsClient, new CancellationTokenSource())
    {
    }

    private McpToolCollection(string name, IReadOnlyList<McpClientTool> tools, McpClient client, bool ownsClient, CancellationTokenSource disposal)
        : base(name, ownsClient ? tools.Select(tool => new OwnedMcpTool(tool, name, disposal.Token)) : tools)
    {
        this.tools = tools;
        this.client = client;
        this.ownsClient = ownsClient;
        this.disposal = disposal;

        // A tool the server marks read-only is read; any other tool gets no kind, so no host runs a server's write as an edit.
        WithKind(ToolKind.Read, [.. tools.Where(tool => tool.ProtocolTool.Annotations is { ReadOnlyHint: true }).Select(tool => tool.Name)]);
    }

    /// <summary>The client the tools call the server through; use it for the server's resources and prompts.</summary>
    /// <exception cref="ObjectDisposedException">A <c>Connect</c> method created the collection, and it was disposed.</exception>
    public McpClient Client => disposal.IsCancellationRequested
        ? throw new ObjectDisposedException(DisplayName, $"The MCP collection '{DisplayName}' was disposed, which disconnected its client; connect a new collection.")
        : client;

    /// <summary>
    /// Makes every tool the server does not mark as safe need the host's approval: all but those its hints call read-only or not
    /// destructive. A tool without hints counts as destructive, as MCP's defaults say.
    /// </summary>
    /// <remarks>
    /// This only adds approval, so a server that lies in its hints cannot make a call less guarded than it would be without this
    /// call. It can still mark a destructive tool read-only, so for a server you do not trust also name such tools in
    /// <see cref="ExternalToolCollection{TSelf}.RequireApproval"/>.
    /// </remarks>
    /// <returns>This collection, for chaining.</returns>
    /// <exception cref="InvalidOperationException">The collection was already given to an agent.</exception>
    public McpToolCollection RequireApprovalForDestructiveTools()
    {
        return RequireApproval([.. tools.Where(IsPossiblyDestructive).Select(tool => tool.Name)]);
    }

    /// <summary>Lists the server's tools and creates a collection from them.</summary>
    /// <param name="name">How messages name the collection, such as the server's name.</param>
    /// <param name="client">A connected client. The collection calls the server through it, and does not dispose it.</param>
    /// <param name="select">
    /// Called once per tool: return the tool, a renamed copy such as <c>tool.WithName("gh_search")</c>, or null to leave it out.
    /// Rename here when two servers share a tool name, or a tool's name has characters model providers reject. Without it every
    /// tool is kept under its own name.
    /// </param>
    /// <param name="includeServerInstructions">
    /// Whether the server's own instructions become the collection's first instruction. They are text from the server placed in
    /// the agent's system prompt, so pass false for a server you do not trust.
    /// </param>
    /// <param name="cancellationToken">Cancels listing the tools.</param>
    /// <exception cref="ModelContextProtocol.McpException">The server failed to list its tools.</exception>
    /// <exception cref="ArgumentException">The name is blank, two kept tools share a name, ignoring case, or a kept name is one model providers reject; the message shows the rename in select.</exception>
    public static Task<McpToolCollection> CreateAsync(
        string name,
        McpClient client,
        Func<McpClientTool, McpClientTool?>? select = null,
        bool includeServerInstructions = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(client);
        return FromClientAsync(client, name, select, includeServerInstructions, ownsClient: false, cancellationToken);
    }

    /// <summary>Connects to an MCP server over the transport and creates a collection that owns the client.</summary>
    /// <param name="name">How messages name the collection, such as the server's name.</param>
    /// <param name="transport">
    /// How to reach the server. For a server over HTTP or a local process, <see cref="ConnectHttpAsync"/> and
    /// <see cref="ConnectStdioAsync"/> build the transport for you.
    /// </param>
    /// <param name="select">Chooses and renames tools, as in <see cref="CreateAsync"/>.</param>
    /// <param name="includeServerInstructions">Whether the server's instructions become the first instruction, as in <see cref="CreateAsync"/>.</param>
    /// <param name="clientOptions">
    /// How the client introduces itself and what it offers the server, such as sampling or elicitation handlers. Null uses the SDK's
    /// defaults.
    /// </param>
    /// <param name="loggerFactory">Where the client and its transport log, such as why a server stopped answering. Null logs nothing.</param>
    /// <param name="cancellationToken">Cancels connecting and listing the tools.</param>
    /// <returns>A collection to dispose when agents no longer use it; disposing it disconnects the client.</returns>
    /// <remarks>If anything fails after the client connects, the client is disconnected before the exception propagates.</remarks>
    /// <exception cref="ModelContextProtocol.McpException">
    /// The client could not connect, with a message naming the server and what to check and the cause as the inner exception; or
    /// the server failed to list its tools.
    /// </exception>
    /// <exception cref="ArgumentException">The name is blank, two kept tools share a name, ignoring case, or a kept name is one model providers reject; the message shows the rename in select.</exception>
    public static Task<McpToolCollection> ConnectAsync(
        string name,
        IClientTransport transport,
        Func<McpClientTool, McpClientTool?>? select = null,
        bool includeServerInstructions = true,
        McpClientOptions? clientOptions = null,
        ILoggerFactory? loggerFactory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(transport);
        return ConnectOwnedAsync(
            name, transport, $"transport '{transport.Name}'", "Check that the server is running and reachable over it; pass a loggerFactory to see what the transport did.",
            select, includeServerInstructions, clientOptions, loggerFactory, cancellationToken);
    }

    private static async Task<McpToolCollection> ConnectOwnedAsync(
        string name,
        IClientTransport transport,
        string target,
        string fix,
        Func<McpClientTool, McpClientTool?>? select,
        bool includeServerInstructions,
        McpClientOptions? clientOptions,
        ILoggerFactory? loggerFactory,
        CancellationToken cancellationToken)
    {
        McpClient client;
        try
        {
            client = await McpClient.CreateAsync(transport, clientOptions, loggerFactory, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Each transport fails differently (HttpRequestException, a process that will not start, a timeout); one type with the
            // server and the fix in its message lets a caller catch it and act on it.
            throw new McpException($"Could not connect to MCP server '{name}' at {target}: {error.Message} {fix}", error);
        }

        try
        {
            return await FromClientAsync(client, name, select, includeServerInstructions, ownsClient: true, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Connects to a remote MCP server over HTTP and creates a collection that owns the client.</summary>
    /// <param name="name">How messages name the collection, such as the server's name.</param>
    /// <param name="endpoint">The server's URL, such as <c>https://mcp.context7.com/mcp</c>.</param>
    /// <param name="headers">
    /// Headers sent with every request, such as an API key. A header whose value is null or blank is left out, so an optional key
    /// read from the environment can go in as it is; the logger notes which ones were left out.
    /// </param>
    /// <param name="httpClient">
    /// The client to send requests with, such as one from <c>IHttpClientFactory</c> with a proxy or retries. The collection does not
    /// dispose it. Null makes one the transport owns.
    /// </param>
    /// <param name="configure">
    /// Changes the transport options after the endpoint and headers are set, for what has no parameter here: the transport mode,
    /// the connection timeout, OAuth, reconnection.
    /// </param>
    /// <param name="select">Chooses and renames tools, as in <see cref="CreateAsync"/>.</param>
    /// <param name="includeServerInstructions">Whether the server's instructions become the first instruction, as in <see cref="CreateAsync"/>.</param>
    /// <param name="clientOptions">
    /// How the client introduces itself and what it offers the server, such as sampling or elicitation handlers. Null uses the SDK's
    /// defaults.
    /// </param>
    /// <param name="loggerFactory">Where the client and its transport log, such as why a server stopped answering. Null logs nothing.</param>
    /// <param name="cancellationToken">Cancels connecting and listing the tools.</param>
    /// <returns>A collection to dispose when agents no longer use it; disposing it disconnects the client.</returns>
    /// <exception cref="ModelContextProtocol.McpException">Connecting failed, naming the endpoint and what to check; or the server failed to list its tools.</exception>
    /// <exception cref="ArgumentException">The name is blank, two kept tools share a name, ignoring case, or a kept name is one model providers reject; the message shows the rename in select.</exception>
    public static Task<McpToolCollection> ConnectHttpAsync(
        string name,
        Uri endpoint,
        IReadOnlyDictionary<string, string?>? headers = null,
        HttpClient? httpClient = null,
        Action<HttpClientTransportOptions>? configure = null,
        Func<McpClientTool, McpClientTool?>? select = null,
        bool includeServerInstructions = true,
        McpClientOptions? clientOptions = null,
        ILoggerFactory? loggerFactory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(endpoint);
        HttpClientTransportOptions options = HttpOptions(name, endpoint, headers, configure, loggerFactory?.CreateLogger<McpToolCollection>());
        HttpClientTransport transport = httpClient is null
            ? new HttpClientTransport(options, loggerFactory)
            : new HttpClientTransport(options, httpClient, loggerFactory, ownsHttpClient: false);
        return ConnectOwnedAsync(
            name, transport, endpoint.ToString(), "Check the URL and that the server is reachable; a 401 or 403 means a missing or wrong key in headers.",
            select, includeServerInstructions, clientOptions, loggerFactory, cancellationToken);
    }

    /// <summary>
    /// Starts a local MCP server process, talks to it over its standard input and output, and creates a collection that owns the
    /// client.
    /// </summary>
    /// <param name="name">How messages name the collection, such as the server's name.</param>
    /// <param name="command">The program that runs the server, such as <c>npx</c>.</param>
    /// <param name="arguments">The program's arguments, such as <c>["-y", "@modelcontextprotocol/server-filesystem", "."]</c>.</param>
    /// <param name="environment">
    /// Variables set for the server process, such as the key it reads. A null value removes a variable the process would otherwise
    /// get.
    /// </param>
    /// <param name="inheritEnvironment">
    /// Whether the server process starts with this process's whole environment, secrets such as your model provider's key
    /// included. Pass false for a server you do not trust: it then gets only what processes need to start, such as <c>PATH</c>
    /// and <c>HOME</c>, plus <paramref name="environment"/>.
    /// </param>
    /// <param name="workingDirectory">The folder the server process starts in; this process's current folder when null.</param>
    /// <param name="standardError">
    /// Called with each line the server writes to standard error, which is where a server that fails to start says why. Null
    /// drops the lines.
    /// </param>
    /// <param name="select">Chooses and renames tools, as in <see cref="CreateAsync"/>.</param>
    /// <param name="includeServerInstructions">Whether the server's instructions become the first instruction, as in <see cref="CreateAsync"/>.</param>
    /// <param name="clientOptions">
    /// How the client introduces itself and what it offers the server, such as sampling or elicitation handlers. Null uses the SDK's
    /// defaults.
    /// </param>
    /// <param name="loggerFactory">Where the client and its transport log, such as why a server stopped answering. Null logs nothing.</param>
    /// <param name="cancellationToken">Cancels starting the server and listing the tools.</param>
    /// <returns>A collection to dispose when agents no longer use it; disposing it stops the server process.</returns>
    /// <exception cref="ModelContextProtocol.McpException">The server did not start or connect, naming the command and what to check; or it failed to list its tools.</exception>
    /// <exception cref="ArgumentException">The name or command is blank, two kept tools share a name, ignoring case, or a kept name is one model providers reject; the message shows the rename in select.</exception>
    public static Task<McpToolCollection> ConnectStdioAsync(
        string name,
        string command,
        IEnumerable<string>? arguments = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        bool inheritEnvironment = true,
        string? workingDirectory = null,
        Action<string>? standardError = null,
        Func<McpClientTool, McpClientTool?>? select = null,
        bool includeServerInstructions = true,
        McpClientOptions? clientOptions = null,
        ILoggerFactory? loggerFactory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        StdioClientTransportOptions options = StdioOptions(name, command, arguments, environment, inheritEnvironment, workingDirectory, standardError);
        return ConnectOwnedAsync(
            name, new StdioClientTransport(options, loggerFactory), $"command '{command}'",
            $"Check that '{command}' is installed and on PATH, and pass standardError to read why the server stopped.",
            select, includeServerInstructions, clientOptions, loggerFactory, cancellationToken);
    }

    /// <summary>
    /// Disconnects the client when a <c>Connect</c> method created it; after that, <see cref="Client"/> and every call of the
    /// collection's tools throw <see cref="ObjectDisposedException"/>, and a call still waiting for the server is stopped. A client
    /// passed to <see cref="CreateAsync"/> stays connected, and its tools keep working. Disposing twice does nothing more.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (!ownsClient || disposal.IsCancellationRequested)
        {
            return;
        }
        await disposal.CancelAsync().ConfigureAwait(false);
        await client.DisposeAsync().ConfigureAwait(false);
    }

    internal static HttpClientTransportOptions HttpOptions(
        string name,
        Uri endpoint,
        IReadOnlyDictionary<string, string?>? headers,
        Action<HttpClientTransportOptions>? configure,
        ILogger? logger)
    {
        Dictionary<string, string> sent = [];
        foreach ((string header, string? value) in headers ?? new Dictionary<string, string?>())
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                sent[header] = value;
            }
            else
            {
                // Leaving a blank key out is the point, but a required one then fails far from here, as a 401 or a rate limit.
                logger?.LogInformation("Left out the blank header {Header} for MCP server {Collection}.", header, name);
            }
        }
        HttpClientTransportOptions options = new() { Name = name, Endpoint = endpoint, AdditionalHeaders = sent.Count == 0 ? null : sent };
        configure?.Invoke(options);
        return options;
    }

    internal static StdioClientTransportOptions StdioOptions(
        string name,
        string command,
        IEnumerable<string>? arguments,
        IReadOnlyDictionary<string, string?>? environment,
        bool inheritEnvironment,
        string? workingDirectory,
        Action<string>? standardError)
    {
        StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        Dictionary<string, string?> variables = new(comparer);
        if (!inheritEnvironment)
        {
            foreach ((string variable, string? value) in StdioClientTransportOptions.GetDefaultEnvironmentVariables())
            {
                variables[variable] = value;
            }
        }
        foreach ((string variable, string? value) in environment ?? new Dictionary<string, string?>())
        {
            // With an inherited environment a null value tells the SDK to remove the variable; without one there is nothing to remove.
            if (value is null && !inheritEnvironment)
            {
                variables.Remove(variable);
            }
            else
            {
                variables[variable] = value;
            }
        }

        return new StdioClientTransportOptions
        {
            Name = name,
            Command = command,
            Arguments = [.. arguments ?? []],
            InheritEnvironmentVariables = inheritEnvironment,
            EnvironmentVariables = inheritEnvironment && variables.Count == 0 ? null : variables,
            WorkingDirectory = workingDirectory,
            StandardErrorLines = standardError,
        };
    }

    private static async Task<McpToolCollection> FromClientAsync(
        McpClient client,
        string name,
        Func<McpClientTool, McpClientTool?>? select,
        bool includeServerInstructions,
        bool ownsClient,
        CancellationToken cancellationToken)
    {
        // The SDK does not document whether it checks the capability itself, so a server without tools is not asked for them.
        IList<McpClientTool> listed = client.ServerCapabilities.Tools is null
            ? []
            : await client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        List<McpClientTool> tools = [];
        foreach (McpClientTool tool in listed)
        {
            McpClientTool? kept = select is null ? tool : select(tool);
            if (kept is not null)
            {
                tools.Add(kept);
            }
        }
        ThrowIfNamesUnusable(name, tools);

        McpToolCollection collection = new(name, tools, client, ownsClient);
        if (includeServerInstructions && !string.IsNullOrWhiteSpace(client.ServerInstructions))
        {
            collection.WithInstruction(client.ServerInstructions);
        }
        return collection;
    }

    /// <inheritdoc />
    public override string? RenameHint(string toolName) =>
        $"rename '{toolName}' of '{DisplayName}' in the select argument that created it, such as " +
        $"tool => tool.Name == \"{toolName}\" ? tool.WithName(\"{UsableToolName(DisplayName.ToLowerInvariant())}_{toolName}\") : tool";

    /// <summary>
    /// Throws, naming the <c>select</c> rename that fixes it, when a kept tool's name is one OpenAI-compatible providers reject or
    /// two kept tools share a name. The base class would catch the second too, but its message cannot point to <c>select</c>.
    /// </summary>
    private static void ThrowIfNamesUnusable(string name, IReadOnlyList<McpClientTool> tools)
    {
        foreach (McpClientTool tool in tools)
        {
            if (!IsUsableToolName(tool.Name))
            {
                throw new ArgumentException(
                    $"Tool '{tool.Name}' of MCP server '{name}' has a name model providers reject: use only letters, digits, '_' and '-', " +
                    $"at most {MaxToolNameLength} characters. Rename it in select, such as " +
                    $"tool => tool.Name == \"{tool.Name}\" ? tool.WithName(\"{UsableToolName(tool.Name)}\") : tool.",
                    "select");
            }
        }

        string? shared = tools.GroupBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1)?.Key;
        if (shared is not null)
        {
            throw new ArgumentException(
                $"Two tools of MCP server '{name}' are named '{shared}', ignoring case. Tool names must be unique; give one a different name " +
                $"in select, such as tool => tool.Name == \"{shared}\" ? tool.WithName(\"{shared}_2\") : tool.",
                "select");
        }
    }


    // MCP's defaults: readOnlyHint false and destructiveHint true, and destructiveHint means something only when not read-only.
    private static bool IsPossiblyDestructive(McpClientTool tool) =>
        tool.ProtocolTool.Annotations is not { ReadOnlyHint: true } and not { DestructiveHint: false };

    private static bool IsUsableToolName(string toolName) =>
        toolName.Length is > 0 and <= MaxToolNameLength && toolName.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    private static string UsableToolName(string toolName)
    {
        string replaced = new([.. toolName.Select(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' ? character : '_')]);
        return replaced.Length <= MaxToolNameLength ? replaced : replaced[..MaxToolNameLength];
    }
}
