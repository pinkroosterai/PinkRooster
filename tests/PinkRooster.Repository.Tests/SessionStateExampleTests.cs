using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents;
using PinkRooster.Agents.Tests.TestSupport;
using PinkRooster.ToolCollections;

namespace PinkRooster.Repository.Tests;

/// <summary>Runs the session-scoped state example of <c>docs/PinkRooster.ToolCollections.md</c>: one agent, two sessions, no state shared.</summary>
public sealed class SessionStateExampleTests
{
    // session-state:begin
    public sealed class NotesTools : ToolCollection
    {
        private sealed record Notes(List<string> Items);

        [Tool("AddNote", "Keeps a note for this conversation.")]
        public string AddNote(string text)
        {
            CurrentNotes().Items.Add(text);
            return "Noted.";
        }

        public override ValueTask<string?> GetContextAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>($"## Notes\n{string.Join("\n", CurrentNotes().Items)}");

        // One instance serves every session, so the notes live in the session of the run in progress.
        private Notes CurrentNotes() => SessionState(() => new Notes([]));
    }
    // session-state:end

    [Fact]
    public void Guide_ShowsTheTestedExample() =>
        Assert.Equal(Block(File.ReadAllText(Path.Combine(FindRepositoryRoot(), "tests", "PinkRooster.Repository.Tests", "SessionStateExampleTests.cs")), @"// session-state:begin\n(?<code>.*?)\s*// session-state:end"),
            Block(File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "PinkRooster.ToolCollections.md")), @"<!-- session-state:begin -->\s*```csharp\n(?<code>.*?)```\s*<!-- session-state:end -->"));

    private static string[] Block(string text, string pattern)
    {
        Match block = Regex.Match(text.ReplaceLineEndings("\n"), pattern, RegexOptions.Singleline);
        Assert.True(block.Success, "A session-state block is missing.");
        string[] lines = [.. block.Groups["code"].Value.Split('\n').Select(line => line.TrimEnd()).Where(line => line.Trim().Length > 0 && !line.StartsWith("using ", StringComparison.Ordinal))];
        int indent = lines.Min(line => line.Length - line.TrimStart().Length);
        return [.. lines.Select(line => line[indent..])];
    }

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

    [Fact]
    public async Task TwoSessionsOnOneAgent_ShareNoNotes()
    {
        ScriptedChatClient model = new(
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c1", "AddNote", new Dictionary<string, object?> { ["text"] = "alpha" })]),
            new ChatMessage(ChatRole.Assistant, "ok"),
            new ChatMessage(ChatRole.Assistant, "second session"));
        AIAgent agent = model.CreateAgent().WithRole("r").WithTools(new NotesTools()).Build();
        AgentSession first = await agent.CreateSessionAsync();
        AgentSession second = await agent.CreateSessionAsync();

        await agent.RunAsync("note alpha", first);
        await agent.RunAsync("what notes?", second);

        // Requests: 0 and 1 belong to the first session's run, 2 to the second's.
        Assert.Contains(model.Requests[1], message => message.Text.Contains("alpha"));
        Assert.DoesNotContain(model.Requests[2], message => message.Text.Contains("alpha"));
        Assert.Contains(model.Requests[2], message => message.Text.StartsWith("The current state of your tools", StringComparison.Ordinal));
    }
}
