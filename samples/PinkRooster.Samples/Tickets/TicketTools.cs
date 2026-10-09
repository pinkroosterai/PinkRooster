using PinkRooster.ToolCollections;

namespace PinkRooster.Samples.Tickets;

/// <summary>The agent's own tools: two methods, one standing line, and a note on the current state that the model gets before every call.</summary>
[ToolCollectionInstruction("Ticket numbers look like PR-123.")]
public sealed class TicketTools(ITicketStore store) : ToolCollection
{
    [Tool("GetTicket", "Returns one ticket by number: its title and state.")]
    public Task<string> GetTicket(string number) => store.DescribeAsync(number);

    [Tool("CloseTicket", "Closes a ticket. It cannot be undone.", RequiresApproval = true)]
    public Task<string> CloseTicket(string number) => store.CloseAsync(number);

    public override async ValueTask<string?> GetContextAsync(CancellationToken cancellationToken) =>
        $"## Open tickets\n{await store.CountOpenAsync(cancellationToken)}";
}
