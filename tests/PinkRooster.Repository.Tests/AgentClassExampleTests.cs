using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using PinkRooster.Agents;
using PinkRooster.Agents.Briefs;

namespace PinkRooster.Repository.Tests;

/// <summary>Compiles and runs the agent-class example of <c>docs/PinkRooster.Agents.md</c> and holds it to the same line budget as the builder quickstart.</summary>
[Collection("ConsoleOut")]
public sealed class AgentClassExampleTests
{
    private const int LineBudget = 8;

    // agent-class:begin
    [AgentRole("You review pull requests.")]
    [AgentInstruction("Quote the line you mean.")]
    public sealed class ReviewerAgent(IChatClient chatClient) : DeclaredAgent(chatClient);
    // agent-class:end

    private static async Task RunExample(IChatClient chatClient)
    {
        // agent-class-run:begin
        var agent = new ReviewerAgent(chatClient);
        var response = await agent.RunAsync("Review #12");
        Console.WriteLine(response.Text);
        // agent-class-run:end
    }

    [Fact]
    public async Task Example_RunsAgainstAChatClient_AndSendsTheBriefItShows()
    {
        RecordingClient chatClient = new("1. A null check is missing.");
        StringWriter output = new();
        TextWriter original = Console.Out;
        Console.SetOut(output);
        try
        {
            await RunExample(chatClient);
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Equal("1. A null check is missing.", output.ToString().Trim());
        Assert.Contains("# Role\nYou review pull requests.", chatClient.Instructions?.ReplaceLineEndings("\n"));
        Assert.Contains("- Quote the line you mean.", chatClient.Instructions);
    }

    [Fact]
    public void Guide_ShowsTheCompiledExample_WithinItsBudget()
    {
        string root = FindRepositoryRoot();
        string[] shown = DocBlock(File.ReadAllText(Path.Combine(root, "docs", "PinkRooster.Agents.md")));
        string source = File.ReadAllText(Path.Combine(root, "tests", "PinkRooster.Repository.Tests", "AgentClassExampleTests.cs"));

        string[] code = [.. shown.Where(line => !line.StartsWith("using ", StringComparison.Ordinal))];

        Assert.Equal([.. SourceBlock(source, "agent-class"), .. SourceBlock(source, "agent-class-run")], code);
                Assert.InRange(code.Length, 1, LineBudget);
    }

    private static string[] DocBlock(string guide)
    {
        Match block = Regex.Match(guide.ReplaceLineEndings("\n"), @"<!-- agent-class:begin -->\s*```csharp\n(?<code>.*?)```\s*<!-- agent-class:end -->", RegexOptions.Singleline);
        Assert.True(block.Success, "docs/PinkRooster.Agents.md has no agent-class block between the agent-class markers.");
        return NonBlank(block.Groups["code"].Value);
    }

    private static string[] SourceBlock(string source, string name)
    {
        Match block = Regex.Match(source.ReplaceLineEndings("\n"), $@"// {Regex.Escape(name)}:begin\n(?<code>.*?)\s*// {Regex.Escape(name)}:end", RegexOptions.Singleline);
        Assert.True(block.Success, $"AgentClassExampleTests.cs lost its '{name}' markers.");
        return NonBlank(block.Groups["code"].Value);
    }

    private static string[] NonBlank(string code) =>
        [.. code.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0)];

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PinkRooster.slnx")))
            {
                return directory.FullName;
            }
        }
        throw new InvalidOperationException("PinkRooster.slnx not found above the test output folder.");
    }

    private sealed class RecordingClient(string reply) : IChatClient
    {
        public string? Instructions { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Instructions = options?.Instructions;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
