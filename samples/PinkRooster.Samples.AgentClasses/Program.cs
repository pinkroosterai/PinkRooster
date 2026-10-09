using Microsoft.Extensions.AI;
using PinkRooster.Agents;
using PinkRooster.Agents.Eventing;
using PinkRooster.Samples.Terminal;
using PinkRooster.ToolCollections;

namespace PinkRooster.Samples.AgentClasses;

public static class Program
{
    public static Task<int> Main() => SampleHost.RunAsync(RunAsync, usesTools: true);

    public static async Task RunAsync(IChatClient chatClient, ISampleConsole console, CancellationToken cancellationToken)
    {
        ReviewerAgent reviewer = new(chatClient, "PinkRooster");

        // A host that did not write the class adds a handler on the instance; Dispose removes it.
        using IDisposable handler = reviewer.OnEvent<ToolCallCompleted>(done => console.WriteLine($"{done.Name}: {done.Status}"));

        // An instance is a MAF AIAgent: RunAsync, streaming, sessions and approvals work on it.
        var response = await reviewer.RunAsync("Review this change:\nif (user == null) return;\nlog(user);", cancellationToken: cancellationToken);
        console.WriteAnswer(response.Text);
        console.WriteLine($"The class saw these tool calls: {string.Join(", ", reviewer.ToolsCalled)}");
    }
}

/// <summary>An agent class: the brief as attributes, an own tool, text that depends on the constructor, own context, and an override that watches its own events.</summary>
[AgentRole("You review pull requests.")]
[AgentObjective("Find bugs before they merge.")]
[AgentInstruction("Quote the line you mean.", "Call CountLines first to see how large the change is.")]
[AgentConstraint("Never approve a change you have not read.")]
[AgentOutputFormat("A numbered list, most severe first.")]
public sealed class ReviewerAgent(IChatClient chatClient, string repository) : DeclaredAgent(chatClient)
{
    /// <summary>The names of the tools the model called, in order.</summary>
    public List<string> ToolsCalled { get; } = [];

    // Attribute text is constant. Text that depends on a constructor argument goes in Configure, which runs once per instance.
    protected override void Configure(AgentBuilder agent) => agent.WithInstruction($"The repository is {repository}.");

    // Public [Tool] methods on the class are its tools.
    [Tool("CountLines", "Counts the lines of a diff or a piece of code.")]
    public int CountLines(string text) => text.Split('\n').Length;

    // The class's own context: asked before every model call, never stored in the session's history.
    protected override ValueTask<string?> GetContextAsync(CancellationToken cancellationToken) => new($"## Repository\n{repository}");

    protected override void OnEvent(AgentEvent item)
    {
        if (item is ToolCallStarted call)
        {
            lock (ToolsCalled)
            {
                ToolsCalled.Add(call.Name);
            }
        }
    }
}
