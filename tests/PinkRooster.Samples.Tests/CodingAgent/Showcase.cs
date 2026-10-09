using Microsoft.Extensions.AI;
using PinkRooster.Agents.Eventing;
using PinkRooster.Agents.Tests.TestSupport;
using PinkRooster.SpectreConsole;
using Sample = PinkRooster.Samples.CodingAgent;

namespace PinkRooster.Samples.Tests.CodingAgent;

/// <summary>
/// The showcase in a temporary workspace: a model settings file with the named models, a recording console, and
/// <c>Program.RunAsync</c> on scripted models. Nothing here uses a key or the network.
/// </summary>
internal sealed class Showcase : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("showcase-test-").FullName;
    private readonly bool ownsRoot = true;
    private readonly Dictionary<string, IChatClient> clients = new(StringComparer.Ordinal);

    public Showcase(params string[] models) : this(
        "{ \"models\": [" + string.Join(", ", (models.Length == 0 ? ["test-model"] : models).Select(name => $"{{ \"model\": \"{name}\", \"endpoint\": \"http://localhost:1/v1\", \"useWhen\": \"For {name} work.\" }}")) + "] }",
        settings: true)
    {
    }

    /// <summary>The showcase with a model settings file of the test's own.</summary>
    public static Showcase WithSettings(string settingsJson) => new(settingsJson, settings: true);

    private Showcase(string settingsJson, bool settings)
    {
        _ = settings;
        Workspace = Path.Combine(root, "workspace");
        Directory.CreateDirectory(Workspace);
        SettingsPath = Path.Combine(root, "settings", "models.json");
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, settingsJson);
    }

    /// <summary>The MCP servers the program is given when the config file names none; none unless a test sets them.</summary>
    public List<Sample.Project.McpServer> McpServers { get; } = [];

    /// <summary>How a server is connected; a test gives a server that runs in the test process. Null connects for real.</summary>
    public Func<Sample.Project.McpServer, CancellationToken, Task<PinkRooster.ToolCollections.Mcp.McpToolCollection>>? ConnectMcp { get; set; }

    public string Workspace { get; }

    public string SettingsPath { get; }

    public RecordingAgentConsole Console { get; } = new();

    public Sample.Interrupt Interrupt { get; } = new();

    // GetFullPath: a test names a file with forward slashes, and on Windows the program reports it with backslashes.
    public string PathOf(string relativePath) => Path.GetFullPath(Path.Combine(Workspace, relativePath));

    public string Write(string relativePath, string content)
    {
        string path = PathOf(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public void Config(string json) => Write(Sample.Project.ProjectConfig.FileName, json);

    /// <summary>The client a model of the settings file runs on; a model without one runs on the client given to <see cref="RunAsync(ScriptedChatClient, string[])"/>.</summary>
    public void Use(string model, IChatClient client) => clients[model] = client;

    /// <summary>Runs the program on <paramref name="model"/> with the lines typed one after the other; input then ends.</summary>
    public Task<int> RunAsync(ScriptedChatClient model, params string[] lines) => RunAsync(model, Console, lines);

    public async Task<int> RunAsync(IChatClient model, IAgentConsole console, params string[] lines)
    {
        foreach (string line in lines)
        {
            Console.Inputs.Enqueue(line);
        }
        return await Sample.Program.RunAsync(
            new Sample.HostSetup
            {
                WorkspaceRoot = Workspace,
                ModelSettingsPath = SettingsPath,
                CreateConsole = _ => console,
                CreateClient = entry => clients.TryGetValue(entry.Model, out IChatClient? client) ? client : model,
                DefaultMcpServers = McpServers,
                ConnectMcpAsync = ConnectMcp ?? Sample.Mcp.McpServers.ConnectAsync,
                Interrupt = Interrupt
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The labels of the steps the console was shown, in order. A step that an approval paused is announced again when it goes on,
    /// so a label that repeats the one before it is the same step.
    /// </summary>
    public string[] Steps
    {
        get
        {
            List<string> labels = [];
            foreach (StepStarted step in Console.Events.OfType<StepStarted>())
            {
                if (labels.Count == 0 || labels[^1] != step.Label)
                {
                    labels.Add(step.Label);
                }
            }
            return [.. labels];
        }
    }

    /// <summary>How many verify steps the conversation has had: each one sent its prompt, which stays in the history.</summary>
    public static int VerifyTurns(ScriptedChatClient model) => model.Requests[^1].Count(message => message.Text.StartsWith("You changed files.", StringComparison.Ordinal));

    /// <summary>A new program on the same workspace and settings, with its own console: the next day.</summary>
    public Showcase Again() => new(this);

    private Showcase(Showcase earlier)
    {
        root = earlier.root;
        ownsRoot = false;
        Workspace = earlier.Workspace;
        SettingsPath = earlier.SettingsPath;
    }

    public void Dispose()
    {
        Interrupt.Dispose();
        if (ownsRoot)
        {
            Directory.Delete(root, recursive: true);
        }
    }

    public static ChatMessage Text(string text) => new(ChatRole.Assistant, text);

    public static ChatMessage Call(string name, params (string Name, object? Value)[] arguments) =>
        new(ChatRole.Assistant, [new FunctionCallContent($"call-{Guid.NewGuid():N}", name, arguments.ToDictionary(argument => argument.Name, argument => argument.Value))]);

    public static ChatMessage Edit(string path, string oldText, string newText) => Call("EditFile", ("path", path), ("oldText", oldText), ("newText", newText));

    public static ChatMessage Shell(string command, bool inBackground = false) =>
        inBackground ? Call("RunShell", ("command", command), ("runInBackground", true)) : Call("RunShell", ("command", command));

    /// <summary>What each tool call of the conversation answered, by tool name, as the model read it in its last request.</summary>
    public static IReadOnlyList<(string Name, string Result)> Results(ScriptedChatClient model)
    {
        List<ChatMessage> last = model.Requests[^1];
        Dictionary<string, string> names = last.SelectMany(message => message.Contents).OfType<FunctionCallContent>().ToDictionary(call => call.CallId, call => call.Name);
        return [.. last.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Select(result => (names[result.CallId], result.Result?.ToString() ?? string.Empty))];
    }

    public static int CountInAll(ScriptedChatClient model, string text) => model.Requests.Sum(request => request.Count(message => message.Text == text));
}
