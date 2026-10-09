using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents.Permissions;
using PinkRooster.ToolCollections;

namespace PinkRooster.Agents.Tests;

public sealed class AgentApprovalExtensionsTests
{
    private sealed class Tickets : ToolCollection
    {
        public List<string> Closed { get; } = [];

        [Tool("CloseTicket", "Closes a ticket.", RequiresApproval = true)]
        public string CloseTicket(string number)
        {
            Closed.Add(number);
            return $"{number} closed.";
        }
    }

    private static ChatMessage Calls(params string[] numbers) =>
        new(ChatRole.Assistant, [.. numbers.Select(number => (AIContent)new FunctionCallContent($"call-{number}", "CloseTicket", new Dictionary<string, object?> { ["number"] = number }))]);

    private static AIAgent Agent(ScriptedChatClient client, Tickets tickets) =>
        client.CreateAgent().WithoutDefaults().WithRole("r").WithTools(tickets).Build();

    [Fact]
    public async Task GetApprovalRequests_ListsWhatTheResponseAsks_AndIsEmptyForAPlainAnswer()
    {
        Tickets tickets = new();
        AIAgent agent = Agent(new ScriptedChatClient(Calls("PR-1", "PR-2"), new ChatMessage(ChatRole.Assistant, "done")), tickets);
        AgentSession session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        AgentResponse asked = await agent.RunAsync("go", session, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["call-PR-1", "call-PR-2"], asked.GetApprovalRequests().Select(request => ((FunctionCallContent)request.ToolCall).CallId));
        Assert.Empty(new AgentResponse(new ChatMessage(ChatRole.Assistant, "plain")).GetApprovalRequests());
    }

    [Fact]
    public async Task RunWithApprovals_AsksAboutEveryCall_RunsTheApprovedOnes_AndReturnsTheLastAnswer()
    {
        Tickets tickets = new();
        ScriptedChatClient client = new(Calls("PR-1", "PR-2"), Calls("PR-3"), new ChatMessage(ChatRole.Assistant, "done"));
        AIAgent agent = Agent(client, tickets);
        List<string> asked = [];

        AgentResponse response = await agent.RunWithApprovalsAsync(
            "go",
            session: null,
            (call, _) =>
            {
                string number = call.Arguments!["number"]!.ToString()!;
                asked.Add(number);
                return Task.FromResult(number != "PR-2");
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("done", response.Text);
        Assert.Equal(["PR-1", "PR-2", "PR-3"], asked);
        Assert.Equal(["PR-1", "PR-3"], tickets.Closed);
        // The refused call reaches the model as a result that says so.
        Assert.Contains(client.Requests.SelectMany(request => request).SelectMany(message => message.Contents).OfType<FunctionResultContent>(),
            result => result.CallId == "call-PR-2" && (result.Result?.ToString() ?? "").Contains("did not allow"));
    }

    [Fact]
    public async Task RunWithApprovals_WithAnAnswerThatCarriesAReason_TellsTheModelThatReason_AndTheUsualOneWithout()
    {
        Tickets tickets = new();
        ScriptedChatClient client = new(Calls("PR-1", "PR-2", "PR-3"), new ChatMessage(ChatRole.Assistant, "done"));

        AgentResponse response = await Agent(client, tickets).RunWithApprovalsAsync(
            "go",
            session: null,
            (call, _) => Task.FromResult(call.Arguments!["number"]!.ToString() switch
            {
                "PR-1" => ApprovalAnswer.Allow,
                "PR-2" => ApprovalAnswer.Refuse("Tickets are frozen until Monday."),
                _ => ApprovalAnswer.Refuse()
            }),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("done", response.Text);
        Assert.Equal(["PR-1"], tickets.Closed);
        Dictionary<string, string> results = client.Requests.SelectMany(request => request).SelectMany(message => message.Contents).OfType<FunctionResultContent>()
            .GroupBy(result => result.CallId).ToDictionary(group => group.Key, group => group.First().Result?.ToString() ?? "");
        Assert.Contains("Tickets are frozen until Monday.", results["call-PR-2"]);
        Assert.Contains("did not allow", results["call-PR-3"]);
    }

    [Fact]
    public async Task RunStreamingWithApprovals_WithAnAnswerThatCarriesAReason_TellsTheModelThatReason()
    {
        Tickets tickets = new();
        ScriptedChatClient client = new(Calls("PR-1"), new ChatMessage(ChatRole.Assistant, "done"));

        AgentResponse response = await Agent(client, tickets)
            .RunStreamingWithApprovalsAsync("go", session: null, (_, _) => Task.FromResult(ApprovalAnswer.Refuse("Not in plan mode.")), cancellationToken: TestContext.Current.CancellationToken)
            .ToAgentResponseAsync(TestContext.Current.CancellationToken);

        Assert.EndsWith("done", response.Text);
        Assert.Empty(tickets.Closed);
        Assert.Contains(client.Requests.SelectMany(request => request).SelectMany(message => message.Contents).OfType<FunctionResultContent>(),
            result => (result.Result?.ToString() ?? "").Contains("Not in plan mode."));
    }

    [Fact]
    public async Task RunWithApprovals_WithoutARequest_RunsOnce_AndNeverAsks()
    {
        ScriptedChatClient client = new(new ChatMessage(ChatRole.Assistant, "nothing to do"));
        AIAgent agent = Agent(client, new Tickets());

        AgentResponse response = await agent.RunWithApprovalsAsync(
            "go", session: null, Task<bool> (_, _) => throw new InvalidOperationException("Nothing needed approval."), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("nothing to do", response.Text);
        Assert.Single(client.Requests);
    }

    [Fact]
    public async Task RunStreamingWithApprovals_YieldsEveryRun_AndContinuesAfterTheAnswer()
    {
        Tickets tickets = new();
        AIAgent agent = Agent(new ScriptedChatClient(Calls("PR-1"), new ChatMessage(ChatRole.Assistant, "done")), tickets);
        AgentSession session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        AgentResponse response = await agent
            .RunStreamingWithApprovalsAsync("go", session, (_, _) => Task.FromResult(true), cancellationToken: TestContext.Current.CancellationToken)
            .ToAgentResponseAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["PR-1"], tickets.Closed);
        Assert.Equal("done", response.Text);
        Assert.Single(response.GetApprovalRequests());
    }

    [Fact]
    public async Task RunWithApprovals_RejectsANullCallback_AndABlankMessage()
    {
        AIAgent agent = Agent(new ScriptedChatClient(new ChatMessage(ChatRole.Assistant, "x")), new Tickets());

        await Assert.ThrowsAsync<ArgumentNullException>(() => agent.RunWithApprovalsAsync("go", null, (Func<FunctionCallContent, CancellationToken, Task<bool>>)null!));
        await Assert.ThrowsAsync<ArgumentException>(() => agent.RunWithApprovalsAsync(" ", null, (_, _) => Task.FromResult(true)));
    }
}
