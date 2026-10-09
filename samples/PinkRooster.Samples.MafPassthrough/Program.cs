using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents;
using PinkRooster.Samples.Terminal;
using PinkRooster.Samples.Tickets;

namespace PinkRooster.Samples.MafPassthrough;

public static class Program
{
    public static Task<int> Main() => SampleHost.RunAsync(RunAsync, usesTools: true);

    public static async Task RunAsync(IChatClient chatClient, ISampleConsole console, CancellationToken cancellationToken)
    {
        AIAgent agent = chatClient
            .CreateAgent()
            .WithRole("You triage support tickets.")
            .WithTools(new TicketTools(new TicketStore()))
            // Where the conversation is kept; here MAF's in-memory provider, which could be one that stores to a database.
            .WithChatHistoryProvider(new InMemoryChatHistoryProvider())
            // Anything MAF's AIContextProvider can add before a run: memory, retrieval, a note.
            .WithContextProvider(new ShopHoursProvider())
            // Middleware on the chat client, around every model call.
            .ConfigureClient(pipeline => pipeline.Use((messages, options, next, cancellationToken) =>
            {
                console.WriteLine($"model call with {messages.Count()} messages");
                return next(messages, options, cancellationToken);
            }))
            // Limits and error detail of the loop that runs tools.
            .ConfigureToolLoop(loop => loop.MaximumIterationsPerRequest = 5)
            // Temperature, response format and the other request options.
            .ConfigureChatOptions(options => options.Temperature = 0.2f)
            // Any other option of the MAF agent.
            .ConfigureAgentOptions(options => options.Description = "Triages support tickets.")
            .Build();

        AgentSession session = await agent.CreateSessionAsync(cancellationToken);
        console.WriteAnswer((await agent.RunAsync("What is the state of PR-7?", session, cancellationToken: cancellationToken)).Text);
        console.WriteAnswer((await agent.RunAsync("And is the shop open now?", session, cancellationToken: cancellationToken)).Text);
        console.WriteLine($"{agent.Description}");
    }

    /// <summary>A MAF context provider: adds a line to the instructions of every run.</summary>
    private sealed class ShopHoursProvider : AIContextProvider
    {
        protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default) =>
            new(new AIContext { Instructions = "The shop is open from 9 to 17 on weekdays." });
    }
}
