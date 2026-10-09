using System.Text.Json;
using PinkRooster.Agents.Permissions;

namespace PinkRooster.Samples.CodingAgent.Project;

/// <summary>An MCP server named in the config file: a URL, or a command with arguments that is started as a child process.</summary>
/// <param name="Name">How the server's tools and messages are named.</param>
/// <param name="Url">The server's HTTP address; null for a server started with <paramref name="Command"/>.</param>
/// <param name="Command">The program to start; null for a server at <paramref name="Url"/>.</param>
/// <param name="Arguments">The program's arguments.</param>
public sealed record McpServer(string Name, Uri? Url, string? Command, IReadOnlyList<string> Arguments);

/// <summary>
/// What a team sets for the assistant in <c>.coding-agent.json</c> in the workspace root: allow rules, MCP servers, the verify
/// command and the turn limit. The file is shared through the repository, which is why it is not under <c>.pinkrooster/</c>.
/// </summary>
/// <param name="AllowRules">Tool calls that run without asking, in every permission mode.</param>
/// <param name="McpServers">The MCP servers to connect to; null when the file names none, so the caller's default applies.</param>
/// <param name="VerifyCommand">The command that checks the project after a change, such as <c>dotnet build &amp;&amp; dotnet test</c>; null lets the assistant find one.</param>
/// <param name="MaxTurns">The most turns one user message takes, the first included.</param>
public sealed record ProjectConfig(IReadOnlyList<AllowRule> AllowRules, IReadOnlyList<McpServer>? McpServers, string? VerifyCommand, int MaxTurns)
{
    public const string FileName = ".coding-agent.json";
    public const int DefaultMaxTurns = 3;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>Reads the file in <paramref name="workspaceRoot"/>; a missing file gives the defaults.</summary>
    /// <exception cref="ArgumentException">The file is not valid JSON or has an entry to fix; the message names it.</exception>
    public static ProjectConfig Load(string workspaceRoot)
    {
        string path = Path.Combine(workspaceRoot, FileName);
        if (!File.Exists(path))
        {
            return new ProjectConfig([], null, null, DefaultMaxTurns);
        }

        Document? document;
        try
        {
            document = JsonSerializer.Deserialize<Document>(File.ReadAllText(path), Options);
        }
        catch (JsonException error)
        {
            throw new ArgumentException($"{path} is not valid JSON: {error.Message}", error);
        }

        if (document?.MaxTurns is < 1)
        {
            throw new ArgumentException($"{path} has a \"maxTurns\" of {document.MaxTurns}; give at least 1, or leave it out for {DefaultMaxTurns}.");
        }

        return new ProjectConfig(
            [.. (document?.Allow ?? []).Select((rule, index) => ToRule(path, index, rule))],
            document?.McpServers is null ? null : [.. document.McpServers.Select((server, index) => ToServer(path, index, server))],
            string.IsNullOrWhiteSpace(document?.VerifyCommand) ? null : document.VerifyCommand.Trim(),
            document?.MaxTurns ?? DefaultMaxTurns);
    }

    // { "tool": "Deploy" } covers the tool; { "tool": "RunShell", "argument": "command", "prefix": "git status" } covers calls that start so.
    private static AllowRule ToRule(string path, int index, RuleEntry? rule)
    {
        string where = $"Allow rule {index + 1} in {path}";
        if (string.IsNullOrWhiteSpace(rule?.Tool))
        {
            throw new ArgumentException($"{where} has no \"tool\". Name the tool, such as \"RunShell\".");
        }
        if (string.IsNullOrWhiteSpace(rule.Prefix))
        {
            return AllowRule.ForTool(rule.Tool);
        }
        if (string.IsNullOrWhiteSpace(rule.Argument))
        {
            throw new ArgumentException($"{where} has a \"prefix\" and no \"argument\". Name the argument the prefix is for, such as \"command\".");
        }
        try
        {
            return AllowRule.ForPrefix(rule.Tool, rule.Argument, rule.Prefix);
        }
        catch (ArgumentException error)
        {
            throw new ArgumentException($"{where}: {error.Message}", error);
        }
    }

    private static McpServer ToServer(string path, int index, ServerEntry? server)
    {
        string where = $"MCP server {index + 1} in {path}";
        if (string.IsNullOrWhiteSpace(server?.Name))
        {
            throw new ArgumentException($"{where} has no \"name\".");
        }
        bool hasUrl = !string.IsNullOrWhiteSpace(server.Url);
        bool hasCommand = !string.IsNullOrWhiteSpace(server.Command);
        if (hasUrl == hasCommand)
        {
            throw new ArgumentException($"{where} needs either a \"url\" or a \"command\", not both and not neither.");
        }
        if (hasUrl && (!Uri.TryCreate(server.Url, UriKind.Absolute, out Uri? url) || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)))
        {
            throw new ArgumentException($"{where} has a \"url\" that is not an absolute HTTP or HTTPS address.");
        }
        return new McpServer(server.Name.Trim(), hasUrl ? new Uri(server.Url!) : null, hasCommand ? server.Command!.Trim() : null, server.Arguments ?? []);
    }

    private sealed record Document(List<RuleEntry?>? Allow, List<ServerEntry?>? McpServers, string? VerifyCommand, int? MaxTurns);

    private sealed record RuleEntry(string? Tool, string? Argument, string? Prefix);

    private sealed record ServerEntry(string? Name, string? Url, string? Command, List<string>? Arguments);
}
