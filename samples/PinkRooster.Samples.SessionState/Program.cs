using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents;
using PinkRooster.Samples.Terminal;
using PinkRooster.ToolCollections;

namespace PinkRooster.Samples.SessionState;

public static class Program
{
    public static Task<int> Main() => SampleHost.RunAsync(RunAsync, usesTools: true);

    public static async Task RunAsync(IChatClient chatClient, ISampleConsole console, CancellationToken cancellationToken)
    {
        NotesTools notes = new();
        AIAgent agent = chatClient
            .CreateAgent()
            .WithRole("You keep notes for the user. Use AddNote to keep one.")
            .WithTools(notes)
            .Build();

        // One agent, two conversations.
        AgentSession tea = await agent.CreateSessionAsync(cancellationToken);
        AgentSession coffee = await agent.CreateSessionAsync(cancellationToken);
        await agent.RunAsync("Note that I like tea.", tea, cancellationToken: cancellationToken);
        await agent.RunAsync("Note that I like coffee.", coffee, cancellationToken: cancellationToken);

        console.WriteLine($"The first session holds: {string.Join("; ", notes.Read(tea))}");
        console.WriteLine($"The second session holds: {string.Join("; ", notes.Read(coffee))}");

        // The notes come back through GetContextAsync, so the first session's answer cannot mention the second's.
        console.WriteAnswer((await agent.RunAsync("What do you know about my taste?", tea, cancellationToken: cancellationToken)).Text);
    }
}

/// <summary>Keeps notes per conversation: the state lives in the session, through <c>SessionState</c>, never in a field.</summary>
public sealed class NotesTools : ToolCollection
{
    private sealed record Notes(List<string> Items);

    [Tool("AddNote", "Keeps a note for this conversation.")]
    public string AddNote(string text)
    {
        Current().Items.Add(text);
        return "Noted.";
    }

    public override ValueTask<string?> GetContextAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<string?>($"## Notes\n{string.Join("\n", Current().Items)}");

    /// <summary>The notes a session holds; the state travels with the session when it is saved and restored.</summary>
    public IReadOnlyList<string> Read(AgentSession session) => SessionState<Notes>(session)?.Items ?? [];

    // One instance serves every session, so the notes live in the session of the run in progress.
    private Notes Current() => SessionState(() => new Notes([]));
}
