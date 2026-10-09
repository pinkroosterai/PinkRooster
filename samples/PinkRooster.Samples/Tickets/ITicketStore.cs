namespace PinkRooster.Samples.Tickets;

/// <summary>Where the sample's tickets live; a real app would put a database or an API behind it.</summary>
public interface ITicketStore
{
    Task<string> DescribeAsync(string number);

    Task<string> CloseAsync(string number);

    Task<int> CountOpenAsync(CancellationToken cancellationToken);
}
