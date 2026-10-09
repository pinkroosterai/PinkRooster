using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using PinkRooster.Agents;
using PinkRooster.ToolCollections.BuiltIn.Shells;

namespace PinkRooster.Repository.Tests;

[Collection("ConsoleOut")]
public sealed partial class QuickstartTests
{
    private const int LineBudget = 8;

    [Fact]
    public async Task Quickstart_RunsAgainstAChatClient()
    {
        IChatClient chatClient = new OneReplyChatClient("Yes, it builds.");
        StringWriter output = new();
        TextWriter original = Console.Out;
        Console.SetOut(output);
        try
        {
            await RunQuickstart(chatClient);
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Equal("Yes, it builds.", output.ToString().Trim());
    }

    [Fact]
    public void Readme_ShowsTheCompiledQuickstart_WithinItsBudget()
    {
        string root = FindRepositoryRoot();
        string[] readme = ReadmeBlock(File.ReadAllText(Path.Combine(root, "docs", "PinkRooster.Agents.md")));
        string[] compiled = QuickstartBody(File.ReadAllText(Path.Combine(root, "tests", "PinkRooster.Repository.Tests", "QuickstartTests.cs")));

        string[] usings = [.. readme.Where(line => line.StartsWith("using PinkRooster", StringComparison.Ordinal))];
        string[] code = [.. readme.Where(line => !line.StartsWith("using ", StringComparison.Ordinal))];

        Assert.Equal(compiled, code);
        Assert.Equal(readme.Count(line => line.StartsWith("using ", StringComparison.Ordinal)), usings.Length);
        Assert.InRange(code.Length, 1, LineBudget);
    }

    // The quickstart exactly as the README shows it, minus its using lines (this file has them at the top).
    private static async Task RunQuickstart(IChatClient chatClient)
    {
        // quickstart:begin
        var agent = chatClient
            .CreateAgent()
            .WithRole("You are a build assistant for this repository.")
            .WithTools(new ShellToolCollection())
            .Build();

        var response = await agent.RunAsync("Does the solution build?");
        Console.WriteLine(response.Text);
        // quickstart:end
    }

    private static string[] ReadmeBlock(string readme)
    {
        Match block = ReadmeQuickstart().Match(readme.ReplaceLineEndings("\n"));
        Assert.True(block.Success, "docs/PinkRooster.Agents.md has no quickstart block between the quickstart markers.");
        return NonBlank(block.Groups["code"].Value);
    }

    private static string[] QuickstartBody(string source)
    {
        Match body = SourceQuickstart().Match(source.ReplaceLineEndings("\n"));
        Assert.True(body.Success, "QuickstartTests.cs lost its quickstart markers.");
        return NonBlank(body.Groups["code"].Value);
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

    [GeneratedRegex(@"<!-- quickstart:begin -->\s*```csharp\n(?<code>.*?)```\s*<!-- quickstart:end -->", RegexOptions.Singleline)]
    private static partial Regex ReadmeQuickstart();

    [GeneratedRegex(@"// quickstart:begin\n(?<code>.*?)\s*// quickstart:end", RegexOptions.Singleline)]
    private static partial Regex SourceQuickstart();

    /// <summary>Answers every request with the same text and never calls a tool.</summary>
    private sealed class OneReplyChatClient(string reply) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
