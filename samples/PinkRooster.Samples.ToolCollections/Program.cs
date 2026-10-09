using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents;
using PinkRooster.Samples.Terminal;
using PinkRooster.ToolCollections;

namespace PinkRooster.Samples.ToolCollections;

public static class Program
{
    public static Task<int> Main() => SampleHost.RunAsync(RunAsync, usesTools: true);

    public static async Task RunAsync(IChatClient chatClient, ISampleConsole console, CancellationToken cancellationToken)
    {
        AgentBuilder builder = chatClient
            .CreateAgent()
            .WithRole("You help customers of an online shop.")
            .WithTools(new OrderTools(
                new Dictionary<string, string> { ["A-1001"] = "shipped, 42.50", ["A-1002"] = "open, 18.00", ["A-1003"] = "open, 7.25" },
                currency: "euro"));

        // The collection's lines are part of the system prompt, after the defaults and before the agent's own.
        console.WriteLine("The system prompt:");
        console.WriteLine(builder.BuildOptions().ChatOptions?.Instructions ?? "");

        AIAgent agent = builder.Build();
        AgentResponse response = await agent.RunAsync("Where is order A-1001?", cancellationToken: cancellationToken);
        console.WriteLine();
        console.WriteAnswer(response.Text);
    }
}

/// <summary>A tool collection: two tools, two fixed lines, one line that depends on the constructor, and a note on the current state.</summary>
[ToolCollectionInstruction("Order numbers look like A-1234.")]
[ToolCollectionConstraint("Never tell a customer about an order that is not theirs.")]
public sealed class OrderTools : ToolCollection
{
    private readonly IReadOnlyDictionary<string, string> orders;

    public OrderTools(IReadOnlyDictionary<string, string> orders, string currency)
    {
        this.orders = orders;
        // Text that depends on how the collection was created is added from the constructor.
        AddInstruction($"Totals are in {currency}.");
    }

    [Tool("GetOrder", "Returns one order by number: its state and total.")]
    public string GetOrder(string number) => orders.TryGetValue(number, out string? order) ? $"{number}: {order}." : $"There is no order {number}.";

    [Tool("ListOpenOrders", "Returns the numbers of all open orders.")]
    public string ListOpenOrders() => string.Join(", ", orders.Where(order => order.Value.StartsWith("open", StringComparison.Ordinal)).Select(order => order.Key));

    // Asked before every model call, tool loop included. Keep it short: it is paid for on every call.
    public override ValueTask<string?> GetContextAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<string?>($"## Open orders\n{orders.Count(order => order.Value.StartsWith("open", StringComparison.Ordinal))}");
}
