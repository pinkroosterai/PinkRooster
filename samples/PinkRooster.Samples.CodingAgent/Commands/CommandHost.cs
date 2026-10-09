using Microsoft.Agents.AI;
using PinkRooster.Agents.Permissions;
using PinkRooster.Agents.Sessions;
using PinkRooster.Samples.CodingAgent.History;
using PinkRooster.SpectreConsole;
using PinkRooster.ToolCollections.BuiltIn.FileOperations;
using PinkRooster.ToolCollections.BuiltIn.TaskLists;
using PinkRooster.ToolCollections.Mcp;

namespace PinkRooster.Samples.CodingAgent.Commands;

/// <summary>What a slash command can reach of the running program.</summary>
public sealed record CommandHost
{
    /// <summary>The screen.</summary>
    public required IAgentConsole Console { get; init; }

    /// <summary>The permission policy, whose mode <c>/mode</c> and <c>/plan</c> change.</summary>
    public required PermissionPolicy Policy { get; init; }

    /// <summary>The per-session task list.</summary>
    public required TaskListToolCollection Tasks { get; init; }

    /// <summary>The file tools, which take a message's changes back for <c>/undo</c>.</summary>
    public required FileOperationsToolCollection Files { get; init; }

    /// <summary>The saved conversations of this workspace.</summary>
    public required SessionStore Store { get; init; }

    /// <summary>What shortens a long conversation on its way to the model.</summary>
    public required Compaction Compaction { get; init; }

    /// <summary>The MCP servers that connected at start.</summary>
    public required IReadOnlyList<McpToolCollection> McpServers { get; init; }

    /// <summary>The directory the assistant works in.</summary>
    public required string WorkspaceRoot { get; init; }

    /// <summary>The conversation on screen; it changes at <c>/clear</c> and <c>/resume</c>.</summary>
    public required Func<AgentSession> Session { get; init; }

    /// <summary>Sends a prompt to the assistant as if it had been typed.</summary>
    public required Func<string, CancellationToken, Task> RunPromptAsync { get; init; }

    /// <summary>Starts a new conversation.</summary>
    public required Func<CancellationToken, Task> ClearAsync { get; init; }

    /// <summary>Goes on with a saved conversation, and returns how the assistant differs from the one that saved it.</summary>
    public required Func<string, CancellationToken, Task<RestoredSession>> ResumeAsync { get; init; }

    /// <summary>Puts a note in front of the next prompt, for something the model must know that the conversation does not show.</summary>
    public required Action<string> NoteForNextPrompt { get; init; }

    /// <summary>The lines of <c>/status</c> that the program itself knows: model, tokens, limits.</summary>
    public required Func<IReadOnlyList<string>> Status { get; init; }
}
