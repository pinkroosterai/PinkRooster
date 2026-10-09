using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents;
using PinkRooster.Agents.Eventing;
using PinkRooster.Samples.Terminal;

namespace PinkRooster.Samples.Middleware;

public static class Program
{
    public static Task<int> Main() => SampleHost.RunAsync(RunAsync);

    public static async Task RunAsync(IChatClient chatClient, ISampleConsole console, CancellationToken cancellationToken)
    {
        AIAgent agent = chatClient
            .CreateAgent()
            .WithRole("You answer customers of an online shop.")
            // Each call wraps the agent so far; the first call sits nearest the agent.
            .Use((inner, services) => new EscalationAgent(inner))
            .OnEvent(item =>
            {
                if (item is Escalated escalated)
                {
                    console.WriteLine($"Escalated to a person: {escalated.Reason}");
                }
            })
            .Build();

        AgentResponse response = await agent.RunAsync("My parcel never arrived. I want my money back.", cancellationToken: cancellationToken);
        console.WriteAnswer(response.Text);
    }
}

/// <summary>An event of your own: derive from <see cref="AgentEvent"/> and publish it from inside a run.</summary>
public sealed record Escalated(string Reason) : AgentEvent;

/// <summary>Agent middleware: asks the inner agent, then escalates a reply that promises a refund.</summary>
public sealed class EscalationAgent(AIAgent inner) : DelegatingAIAgent(inner)
{
    protected override async Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        AgentResponse response = await base.RunCoreAsync(messages, session, options, cancellationToken).ConfigureAwait(false);
        if (response.Text.Contains("refund", StringComparison.OrdinalIgnoreCase))
        {
            // Publishes to this agent's handlers, with the run's id filled in; false when no run is in progress.
            AgentEvents.Publish(new Escalated("The reply promises a refund."));
        }

        return response;
    }
}
