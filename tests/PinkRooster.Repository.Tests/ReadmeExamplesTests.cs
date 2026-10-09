using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents;
using PinkRooster.Agents.Eventing;
using PinkRooster.Agents.Tests.TestSupport;
using PinkRooster.ToolCollections.BuiltIn.Shells;
using PinkRooster.ToolCollections;

namespace PinkRooster.Repository.Tests;

/// <summary>
/// Redirects the process-global <c>Console.Out</c>, so it shares a collection with <see cref="QuickstartTests"/>: xunit never
/// runs the two classes in parallel and the captures cannot interleave.
/// </summary>
[Collection("ConsoleOut")]
public sealed partial class ReadmeExamplesTests
{
    private static readonly string Root = FindRepositoryRoot();

    // ---- The examples, exactly as the guides show them (the using lines are at the top of this file).

    // ticket-tools:begin
    [ToolCollectionInstruction("Ticket numbers look like PR-123.")]
    public sealed class TicketTools(ITicketStore store) : ToolCollection
    {
        [Tool("GetTicket", "Returns one ticket by number: its title, state and assignee.")]
        public Task<string> GetTicket(string number) => store.DescribeAsync(number);

        [Tool("CloseTicket", "Closes a ticket. It cannot be undone.", RequiresApproval = true)]
        public Task<string> CloseTicket(string number) => store.CloseAsync(number);

        public override async ValueTask<string?> GetContextAsync(CancellationToken cancellationToken) =>
            $"## Open tickets\n{await store.CountOpenAsync(cancellationToken)}";
    }
    // ticket-tools:end

    private static async Task<AIAgent> TicketAgent(IChatClient chatClient, ITicketStore store)
    {
        await Task.CompletedTask;
        // ticket-agent:begin
        AIAgent agent = chatClient
            .CreateAgent()
            .WithRole("You triage support tickets.")
            .WithConstraint("Never close a ticket you have not read.")
            .WithTools(new TicketTools(store))
            .Build();
        // ticket-agent:end
        return agent;
    }

    private static async Task RunWithEvents(IChatClient chatClient, ITicketStore store)
    {
        // events:begin
        AIAgent agent = chatClient
            .CreateAgent()
            .WithRole("You triage support tickets.")
            .WithTools(new TicketTools(store))
            .OnEvent(item =>
            {
                switch (item)
                {
                    case ToolCallStarted call: Console.WriteLine($"-> {call.Name}"); break;
                    case ToolCallCompleted done: Console.WriteLine($"<- {done.Name}: {done.Status}"); break;
                    case AssistantTextDelta text: Console.Write(text.Text); break;
                }
            })
            .Build();

        await agent.RunAsync("Who has PR-7?");
        // events:end
    }

    // ---- The checks.

    [Fact]
    public void QuickstartInTheReadme_IsTheGuidesTestedBlock() =>
        Assert.Equal(DocBlock("docs/PinkRooster.Agents.md", "quickstart"), DocBlock("README.md", "quickstart"));

    [Fact]
    public void TicketTools_AreTheGuidesCollection() =>
        Assert.Equal(SourceBlock("ticket-tools"), DocBlock("docs/PinkRooster.ToolCollections.md", "ticket-tools"));

    [Fact]
    public void TicketAgent_IsTheGuidesAgent() =>
        Assert.Equal(SourceBlock("ticket-agent"), DocBlock("docs/PinkRooster.ToolCollections.md", "ticket-agent"));

    [Fact]
    public async Task TheModelSees_WhatTheGuideShows()
    {
        ScriptedChatClient chat = new(new ChatMessage(ChatRole.Assistant, "Twelve tickets are open."));
        AIAgent agent = await TicketAgent(chat, new FakeTickets());

        await agent.RunAsync("How many tickets are open?");

        string seen = string.Join("\n", ["system prompt:", chat.Options[0]?.Instructions ?? "", "", "messages:", .. chat.Requests[0].Select(message => $"[{message.Role}] {message.Text}")]);
        Assert.Equal(DocBlock("docs/PinkRooster.ToolCollections.md", "ticket-prompt", "text"), NonBlank(seen));
    }

    [Fact]
    public void Events_AreTheGuidesExample() =>
        Assert.Equal(SourceBlock("events"), DocBlock("docs/PinkRooster.Agents.md", "events"));

    [Fact]
    public async Task Events_PrintWhatTheGuideShows()
    {
        ScriptedChatClient chat = new(
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "GetTicket", new Dictionary<string, object?> { ["number"] = "PR-7" })]),
            new ChatMessage(ChatRole.Assistant, "PR-7 is open and assigned to Ana."));

        string printed = await Capture(() => RunWithEvents(chat, new FakeTickets()));

        Assert.Equal(DocBlock("docs/PinkRooster.Agents.md", "events-output", "text"), NonBlank(printed));
    }

    // ---- Helpers.

    private sealed class FakeTickets : ITicketStore
    {
        public Task<string> DescribeAsync(string number) => Task.FromResult($"{number}: Login fails on Safari. State: open. Assignee: Ana.");
        public Task<string> CloseAsync(string number) => Task.FromResult($"{number} closed.");
        public Task<int> CountOpenAsync(CancellationToken cancellationToken) => Task.FromResult(12);
    }

    public interface ITicketStore
    {
        Task<string> DescribeAsync(string number);
        Task<string> CloseAsync(string number);
        Task<int> CountOpenAsync(CancellationToken cancellationToken);
    }

    private static async Task<string> Capture(Func<Task> action)
    {
        StringWriter output = new();
        TextWriter original = Console.Out;
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        Console.SetOut(output);
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; // the README shows invariant number formats
        try
        {
            await action();
        }
        finally
        {
            Console.SetOut(original);
            CultureInfo.CurrentCulture = originalCulture;
        }

        return output.ToString();
    }

    private static string[] SourceBlock(string name)
    {
        string source = File.ReadAllText(Path.Combine(Root, "tests", "PinkRooster.Repository.Tests", "ReadmeExamplesTests.cs")).ReplaceLineEndings("\n");
        Match block = Regex.Match(source, $@"// {Regex.Escape(name)}:begin\n(?<code>.*?)\s*// {Regex.Escape(name)}:end", RegexOptions.Singleline);
        Assert.True(block.Success, $"ReadmeExamplesTests.cs has no '{name}' block.");
        return NonBlank(block.Groups["code"].Value);
    }

    private static string[] DocBlock(string file, string name, string language = "csharp")
    {
        string text = File.ReadAllText(Path.Combine(Root, file)).ReplaceLineEndings("\n");
        Match block = Regex.Match(text, $@"<!-- {Regex.Escape(name)}:begin -->\s*```{language}\n(?<code>.*?)```\s*<!-- {Regex.Escape(name)}:end -->", RegexOptions.Singleline);
        Assert.True(block.Success, $"{file} has no '{name}' block.");
        return NonBlank(string.Join("\n", block.Groups["code"].Value.Split('\n').Where(line => !line.StartsWith("using ", StringComparison.Ordinal))));
    }

    private static string[] NonBlank(string code) =>
        [.. code.ReplaceLineEndings("\n").Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0)];

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
}
