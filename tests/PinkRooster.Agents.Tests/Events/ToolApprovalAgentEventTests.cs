using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents.Eventing;
using PinkRooster.Agents.Tests.TestSupport;

namespace PinkRooster.Agents.Tests.Events;

/// <summary>MAF's <see cref="ToolApprovalAgent"/>, added with <c>Use</c>, queues approval requests and shows them one at a time; each is published once.</summary>
public sealed class ToolApprovalAgentEventTests
{
    private static ChatMessage Text(string text) => new(ChatRole.Assistant, text);

    private static ChatMessage TwoCalls() => new(ChatRole.Assistant,
    [
        new FunctionCallContent("c1", "Ping", new Dictionary<string, object?> { ["who"] = "a" }),
        new FunctionCallContent("c2", "Ping", new Dictionary<string, object?> { ["who"] = "b" })
    ]);

    private static AIAgent Build(ScriptedChatClient model, List<AgentEvent> events) =>
        model.CreateAgent().WithRole("r")
            .WithTool(AIFunctionFactory.Create((string who) => $"pong {who}", "Ping"))
            .RequireApproval("Ping")
            .Use((inner, _) => new ToolApprovalAgent(inner))
            .OnEvent(events.Add)
            .Build();

    private static async Task<(string Text, ToolApprovalRequestContent[] Requests)> Run(AIAgent agent, ChatMessage message, AgentSession session, bool streaming)
    {
        if (!streaming)
        {
            AgentResponse response = await agent.RunAsync(message, session);
            return (response.Text, [.. response.Messages.SelectMany(item => item.Contents).OfType<ToolApprovalRequestContent>()]);
        }

        string text = string.Empty;
        List<ToolApprovalRequestContent> requests = [];
        await foreach (AgentResponseUpdate update in agent.RunStreamingAsync(message, session))
        {
            text += update.Text;
            requests.AddRange(update.Contents.OfType<ToolApprovalRequestContent>());
        }
        return (text, [.. requests]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwoApprovalsInOneModelReply_ArePublishedOnceEach_AsMafShowsThemOneAtATime(bool streaming)
    {
        ScriptedChatClient model = new(TwoCalls(), Text("done"));
        List<AgentEvent> events = [];
        AIAgent agent = Build(model, events);
        AgentSession session = await agent.CreateSessionAsync();

        ToolApprovalRequestContent firstRequest = Assert.Single((await Run(agent, new ChatMessage(ChatRole.User, "go"), session, streaming)).Requests);
        ToolApprovalRequestContent secondRequest = Assert.Single((await Run(agent, new ChatMessage(ChatRole.User, [firstRequest.CreateResponse(true)]), session, streaming)).Requests);
        (string text, ToolApprovalRequestContent[] none) = await Run(agent, new ChatMessage(ChatRole.User, [secondRequest.CreateResponse(true)]), session, streaming);

        Assert.Equal("done", text);
        Assert.Empty(none);
        Assert.NotEqual(((FunctionCallContent)firstRequest.ToolCall).CallId, ((FunctionCallContent)secondRequest.ToolCall).CallId);
        Assert.Equal(["c1", "c2"], events.OfType<ToolApprovalRequested>().Select(item => item.CallId));
        Assert.Equal([RunOutcome.AwaitingApproval, RunOutcome.AwaitingApproval, RunOutcome.Succeeded], events.OfType<RunCompleted>().Select(item => item.Outcome));
    }
}
