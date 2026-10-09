// Agent class template only.
// Remove members that the agent does not actually need.
// Do not invent business logic, tools, providers, or steps to fill the template.

using Microsoft.Extensions.AI;
using PinkRooster.Agents;
using PinkRooster.Agents.Eventing;
using PinkRooster.Agents.Steps;
using PinkRooster.ToolCollections;

namespace Example;

// The brief is constant text. Text that depends on a constructor argument goes in Configure.
[AgentRole("You handle the Example domain capability.")]
[AgentObjective("State what a finished request looks like.")]
[AgentInstruction("A non-obvious rule that spans the agent.")]
[AgentConstraint("A boundary the model must observe; hard enforcement belongs in code or approvals.")]
public sealed class ExampleAgent(IChatClient chatClient, string workingDirectory) : DeclaredAgent(chatClient)
{
    // Configure runs once per instance, after the attributes and the own tools, so it wins a clash.
    protected override void Configure(AgentBuilder agent) => agent
        .WithDescription("Handles the Example domain capability.")
        .WithInstruction($"The working directory is {workingDirectory}.");
        // .WithTools(new SomeToolCollection(workingDirectory))
        // .RequireApproval("SomeTool")

    // Public [Tool] methods are the agent's own tools.
    [Tool("Describes what the tool does and when to use it.")]
    public string Lookup(string key) => key;

    // One instance serves every session: read the session of the run in progress, never keep run state in a field.
    // protected override ValueTask<string?> GetContextAsync(CancellationToken cancellationToken) =>
    //     new($"## Current state\n{AIAgent.CurrentRunContext?.Session?.StateBag}");

    // Observes this agent's own events; it cannot change the run.
    // protected override void OnEvent(AgentEvent item) { }
}

// An agent with steps of its own:
// public sealed class ExampleSteppedAgent(IChatClient chatClient) : SteppedAgent(chatClient)
// {
//     protected override int MaxTurns => 3;
//     // StartAsync is optional: override it to set state or enter a first step; otherwise the loop enters "start".
//     protected override ValueTask<NextStep> NextAsync(StepContext step, CancellationToken cancellationToken) =>
//         ValueTask.FromResult(NextStep.Stop());
// }
