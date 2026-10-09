using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using PinkRooster.Agents.Eventing;
using PinkRooster.Agents.Tests.TestSupport;

namespace PinkRooster.Agents.Tests.Events;

/// <summary>A built agent is a plain MAF agent: it runs as a node of an <see cref="AgentWorkflowBuilder"/> workflow and keeps publishing its events there.</summary>
public sealed class WorkflowNodeTests
{
    private static ChatMessage Text(string text) => new(ChatRole.Assistant, text);

    [Fact]
    public async Task BuiltAgents_RunAsNodesOfASequentialWorkflow_EachSeeingTheEarlierReply()
    {
        ScriptedChatClient firstModel = new(Text("outline"));
        ScriptedChatClient secondModel = new(Text("polished"));
        List<AgentEvent> events = [];
        AIAgent first = firstModel.CreateAgent().WithRole("You outline.").WithName("outliner").OnEvent(events.Add).Build();
        AIAgent second = secondModel.CreateAgent().WithRole("You polish.").WithName("polisher").OnEvent(events.Add).Build();
        Workflow workflow = AgentWorkflowBuilder.BuildSequential(first, second);

        await using StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, new List<ChatMessage> { new(ChatRole.User, "Write about tea.") });
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
        List<WorkflowEvent> seen = [];
        await foreach (WorkflowEvent item in run.WatchStreamAsync())
        {
            seen.Add(item);
        }

        Assert.DoesNotContain(seen, item => item is WorkflowErrorEvent);
        Assert.Single(firstModel.Requests);
        Assert.Contains(secondModel.Requests[0], message => message.Text == "outline");
        Assert.Equal(["outliner", "polisher"], events.OfType<RunStarted>().Select(item => item.AgentName));
        Assert.Equal(2, events.OfType<RunCompleted>().Count(item => item.Outcome == RunOutcome.Succeeded));
    }
}
