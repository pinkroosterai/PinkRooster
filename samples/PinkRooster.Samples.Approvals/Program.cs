using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents;
using PinkRooster.Samples.Terminal;
using PinkRooster.SpectreConsole;
using PinkRooster.Samples.Tickets;

namespace PinkRooster.Samples.Approvals;

public static class Program
{
    /// <summary>The sample asks the user, so it needs an interactive terminal.</summary>
    public const bool NeedsTerminal = true;

    public static Task<int> Main() => SampleHost.RunAsync(RunAsync, NeedsTerminal, usesTools: true);

    public static async Task RunAsync(IChatClient chatClient, ISampleConsole console, CancellationToken cancellationToken)
    {
        AIAgent agent = chatClient
            .CreateAgent()
            .WithRole("You triage support tickets.")
            // CloseTicket carries RequiresApproval = true on its [Tool] attribute.
            .WithTools(new TicketTools(new TicketStore()))
            // A tool with no attribute can be marked on the builder, by name.
            .WithTool(ArchiveTicket, "ArchiveTicket", "Moves a closed ticket to the archive.")
            .RequireApproval("ArchiveTicket")
            .Build();

        // An approval ends the run. RunWithApprovalsAsync asks the console about each call, sends the answers back on the same session,
        // and repeats until a run ends without asking.
        AgentSession session = await agent.CreateSessionAsync(cancellationToken);
        AgentResponse response = await agent.RunWithApprovalsAsync(
            "Close PR-8 if it is only a typo, then archive it.", session, console.ConfirmToolCallAsync, cancellationToken: cancellationToken);

        console.WriteAnswer(response.Text);
    }

    private static string ArchiveTicket(string number) => $"{number} archived.";
}
