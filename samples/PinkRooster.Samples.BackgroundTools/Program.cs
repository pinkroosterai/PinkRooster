using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents;
using PinkRooster.Samples.Terminal;

namespace PinkRooster.Samples.BackgroundTools;

public static class Program
{
    /// <summary>How long one count takes: a random 4 to 16 seconds, so the three overlap and end in no fixed order. The tests shorten it.</summary>
    public static Func<TimeSpan> CountTime { get; set; } = () => TimeSpan.FromSeconds(Random.Shared.Next(4, 17));

    public static Task<int> Main() => SampleHost.RunAsync(RunAsync, usesTools: true);

    public static async Task RunAsync(IChatClient chatClient, ISampleConsole console, CancellationToken cancellationToken)
    {
        AIAgent agent = chatClient
            .CreateAgent()
            .WithRole("You prepare the weekly support report.")
            .WithTool(CountTickets, "CountTickets", "Counts last week's tickets for one product. Takes a few seconds.")
            // CountTickets gets one more optional parameter, runInBackground, and the agent gets the tools to follow a task:
            // WaitForTasks, GetTaskResult, ListTasks and CancelTask.
            .AllowBackground("CountTickets")
            // Every limit has a default; these two are the ones a host most often sets.
            .ConfigureBackground(limits =>
            {
                limits.MaxRunningTasks = 3;
                limits.TaskTimeLimit = TimeSpan.FromMinutes(1);
            })
            // The console draws a line when a task starts and when it ends.
            .OnEvent(console.WriteEvent)
            .Build();

        // One run: it lasts until the model has answered and no task is running any more. The console draws the answer from the events.
        await agent.RunAsync(
            "Count last week's tickets for the products Atlas, Borealis and Cirrus, all three at the same time, and give me the three numbers.",
            cancellationToken: cancellationToken);
    }

    private static async Task<string> CountTickets(string product, CancellationToken cancellationToken)
    {
        await Task.Delay(CountTime(), cancellationToken);
        // A fixed number per product, so a run can be checked by eye.
        return $"{product}: {product.Length * 3} tickets";
    }
}
