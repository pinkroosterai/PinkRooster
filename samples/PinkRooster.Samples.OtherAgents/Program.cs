using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Samples.Terminal;
using PinkRooster.Samples.Tickets;
using PinkRooster.ToolCollections.Context;

namespace PinkRooster.Samples.OtherAgents;

public static class Program
{
    public static Task<int> Main() => SampleHost.RunAsync(RunAsync, usesTools: true);

    public static async Task RunAsync(IChatClient chatClient, ISampleConsole console, CancellationToken cancellationToken)
    {
        TicketTools tickets = new(new TicketStore());

        // One way: the collection goes in through MAF's context provider, once per run. This agent was not made by AgentBuilder.
        ChatClientAgent viaProvider = new(chatClient, new ChatClientAgentOptions
        {
            ChatOptions = new ChatOptions { Instructions = "You triage support tickets." },
            AIContextProviders = [new ToolCollectionContextProvider(tickets)]
        });
        console.WriteLine("Through the context provider:");
        console.WriteAnswer((await viaProvider.RunAsync("What is the state of PR-7?", cancellationToken: cancellationToken)).Text);

        // The other: the collection's context on every model call, tool loop included. The tools are passed by hand.
        IChatClient withContext = chatClient.AsBuilder().UseToolCollections(tickets).Build();
        ChatClientAgent viaClient = new(withContext, new ChatClientAgentOptions
        {
            ChatOptions = new ChatOptions { Instructions = "You triage support tickets.", Tools = [.. tickets.GetAIFunctions()] }
        });
        console.WriteLine();
        console.WriteLine("Through the client:");
        console.WriteAnswer((await viaClient.RunAsync("And PR-8?", cancellationToken: cancellationToken)).Text);
    }
}
