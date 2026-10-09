using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents;
using PinkRooster.Samples.Terminal;

namespace PinkRooster.Samples.Builder;

public static class Program
{
    public static Task<int> Main() => SampleHost.RunAsync(RunAsync);

    public static async Task RunAsync(IChatClient chatClient, ISampleConsole console, CancellationToken cancellationToken)
    {
        // Each section of the system prompt has its own call. They are always sent in the same order, whatever the order of the calls.
        AgentBuilder reviewer = chatClient
            .CreateAgent()
            .WithRole("You review pull requests.")
            .WithObjective("Find bugs before they merge.")
            .WithBackground("The repository is a .NET library. Warnings are errors.")
            .WithInstruction("Quote the line you mean.")
            .WithConstraint("Never approve a change you have not read.")
            .WithOutputFormat("A numbered list, most severe first.")
            .WithExample("Review #12", "1. A null check is missing.")
            .Apply(HouseRules);

        // BuildOptions shows what the model will be told, and builds no agent.
        console.WriteLine("The system prompt:");
        console.WriteLine(reviewer.BuildOptions().ChatOptions?.Instructions ?? "");

        // Clone copies a builder, so one base serves several agents.
        AIAgent strict = reviewer.Clone().WithConstraint("Block any change that has no test.").Build();
        AgentResponse response = await strict.RunAsync("Review this change: `if (user == null) return;`", cancellationToken: cancellationToken);
        console.WriteLine();
        console.WriteAnswer(response.Text);

        // The built-in defaults can be dropped, or the whole prompt written as one text instead of sections.
        AgentBuilder bare = chatClient.CreateAgent().WithoutDefaults().WithRole("You answer in one word.");
        AgentBuilder raw = chatClient.CreateAgent().WithSystemPrompt("Answer in one word.");
        console.WriteLine();
        console.WriteLine($"Without defaults: {bare.BuildOptions().ChatOptions?.Instructions?.Length} characters; as a raw prompt: {raw.BuildOptions().ChatOptions?.Instructions?.Length} characters.");
    }

    // Shared setup goes in one method that every agent factory applies.
    private static void HouseRules(AgentBuilder agent) => agent.WithInstruction("Answer in plain English.");
}
