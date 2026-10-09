using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents;
using PinkRooster.Agents.Eventing;
using PinkRooster.Samples.Terminal;
using PinkRooster.Samples.Tickets;

namespace PinkRooster.Samples.Events;

public static class Program
{
    public static Task<int> Main() => SampleHost.RunAsync(RunAsync, usesTools: true);

    public static async Task RunAsync(IChatClient chatClient, ISampleConsole console, CancellationToken cancellationToken)
    {
        AIAgent agent = chatClient
            .CreateAgent()
            .WithRole("You triage support tickets.")
            .WithTools(new TicketTools(new TicketStore()))
            // One handler sees every event; match on the type for what you want.
            .OnEvent(item =>
            {
                switch (item)
                {
                    case RunStarted: console.WriteLine("run started"); break;
                    case ModelCallCompleted call: console.WriteLine($"model call finished: {call.FinishReason}"); break;
                    case ToolCallStarted call: console.WriteLine($"-> {call.Name}"); break;
                    case ToolCallCompleted done: console.WriteLine($"<- {done.Name}: {done.Status}"); break;
                    case RunCompleted run: console.WriteLine($"run {run.Outcome}"); break;
                }
            })
            // Or one handler for one type, and the types derived from it.
            .OnEvent<AssistantTextCompleted>(text => console.WriteLine($"answer: {text.Text}"))
            .Build();

        console.WriteLine("RunAsync:");
        await agent.RunAsync("What is the state of PR-7?", cancellationToken: cancellationToken);

        // A streamed run publishes the same events, with the text also arriving piece by piece.
        console.WriteLine();
        console.WriteLine("RunStreamingAsync:");
        await foreach (AgentResponseUpdate update in agent.RunStreamingAsync("And PR-8?", cancellationToken: cancellationToken))
        {
            console.Write(update.Text);
        }
        console.WriteLine();
    }
}
