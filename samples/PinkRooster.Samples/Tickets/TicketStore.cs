namespace PinkRooster.Samples.Tickets;

/// <summary>Three tickets in memory.</summary>
public sealed class TicketStore : ITicketStore
{
    private readonly Dictionary<string, (string Title, bool Open)> tickets = new()
    {
        ["PR-7"] = ("Login fails on Safari", true),
        ["PR-8"] = ("Typo on the pricing page", true),
        ["PR-9"] = ("Export button does nothing", false)
    };

    public Task<string> DescribeAsync(string number) => Task.FromResult(
        tickets.TryGetValue(number, out (string Title, bool Open) ticket)
            ? $"{number}: {ticket.Title}. State: {(ticket.Open ? "open" : "closed")}."
            : $"There is no ticket {number}.");

    public Task<string> CloseAsync(string number)
    {
        if (!tickets.TryGetValue(number, out (string Title, bool Open) ticket))
        {
            return Task.FromResult($"There is no ticket {number}.");
        }
        tickets[number] = (ticket.Title, Open: false);
        return Task.FromResult($"{number} closed.");
    }

    public Task<int> CountOpenAsync(CancellationToken cancellationToken) => Task.FromResult(tickets.Values.Count(ticket => ticket.Open));
}
