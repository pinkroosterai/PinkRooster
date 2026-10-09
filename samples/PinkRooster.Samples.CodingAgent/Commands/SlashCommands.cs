using Microsoft.Extensions.AI;
using PinkRooster.Agents.Permissions;
using PinkRooster.Agents.Sessions;
using PinkRooster.Samples.CodingAgent.Permissions;
using PinkRooster.Samples.CodingAgent.Project;
using PinkRooster.Samples.CodingAgent.Sessions;
using PinkRooster.ToolCollections;
using PinkRooster.ToolCollections.BuiltIn.AskUserQuestion;
using PinkRooster.ToolCollections.BuiltIn.FileOperations;
using PinkRooster.ToolCollections.BuiltIn.TaskLists;
using PinkRooster.ToolCollections.Mcp;

namespace PinkRooster.Samples.CodingAgent.Commands;

/// <summary>The lines that start with <c>/</c>: handled here, by the host, without a model call unless the command says so.</summary>
public sealed class SlashCommands(CommandHost host)
{
    private const string CarryOut = "Carry out the plan";
    private const string Revise = "Revise it";
    private const string Leave = "Leave it";
    private const int MaxOffered = 10;

    private static readonly (string Name, string Help)[] Known =
    [
        ("/help", "Lists the commands."),
        ("/plan <request>", "Switches to plan mode and runs the request: the assistant explores and answers with a plan, changing nothing."),
        ("/mode [ask|accept-edits|plan]", "Shows or sets the permission mode."),
        ("/tasks", "Shows the task list of this conversation."),
        ("/status", "Shows the model, the permission mode with its allow rules, and the tokens used."),
        ("/init", $"Has the assistant explore the repository and write {ProjectInstructions.FileName}."),
        ("/resume", "Goes on with a saved conversation of this workspace."),
        ("/clear", $"Starts a new conversation, reads {ProjectInstructions.FileName} again and sets the mode back to ask."),
        ("/compact", "Shortens what the next request sends: older tool results first, then a summary."),
        ("/undo", "Takes back the file changes of the last message that changed files. A second /undo takes back the one before."),
        ("/diff", "Shows the working tree's changes, untracked files included."),
        ("/mcp", "Lists the MCP servers and their tools."),
        ("/exit", "Ends the program.")
    ];

    /// <summary>Whether a typed line is a command and not a prompt for the model.</summary>
    public static bool IsCommand(string line) => line.TrimStart().StartsWith('/');

    /// <summary>Runs one command line. Returns false when the program should end.</summary>
    public async Task<bool> RunAsync(string line, CancellationToken cancellationToken)
    {
        string[] parts = line.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
        string argument = parts.Length > 1 ? parts[1].Trim() : string.Empty;
        switch (parts[0].ToLowerInvariant())
        {
            case "/help":
                WriteHelp();
                return true;
            case "/exit":
            case "/quit":
                return false;
            case "/mode":
                Mode(argument);
                return true;
            case "/tasks":
                Tasks();
                return true;
            case "/diff":
                await DiffAsync(cancellationToken).ConfigureAwait(false);
                return true;
            case "/init":
                await host.RunPromptAsync(CodingAssistant.InitPrompt, cancellationToken).ConfigureAwait(false);
                return true;
            case "/clear":
                await host.ClearAsync(cancellationToken).ConfigureAwait(false);
                host.Console.WriteLine("New conversation. The permission mode is ask.");
                return true;
            case "/plan":
                await PlanAsync(argument, cancellationToken).ConfigureAwait(false);
                return true;
            case "/status":
                Status();
                return true;
            case "/resume":
                await ResumeAsync(cancellationToken).ConfigureAwait(false);
                return true;
            case "/compact":
                host.Compaction.CompactNext(host.Session());
                host.Console.WriteLine("The next request is shortened before it is sent. The saved conversation stays whole.");
                return true;
            case "/undo":
                await UndoAsync(cancellationToken).ConfigureAwait(false);
                return true;
            case "/mcp":
                Mcp();
                return true;
            default:
                host.Console.WriteLine($"Unknown command {parts[0]}. The commands are:");
                WriteHelp();
                return true;
        }
    }

    private void WriteHelp()
    {
        foreach ((string name, string help) in Known)
        {
            host.Console.WriteLine($"  {name,-32} {help}");
        }
    }

    private void Mode(string argument)
    {
        if (argument.Length == 0)
        {
            host.Console.WriteLine($"The permission mode is {PermissionModes.NameOf(host.Policy.Mode)}. {PermissionModes.Describe(host.Policy.Mode)}");
            host.Console.WriteLine($"Set it with /mode {string.Join(", /mode ", PermissionModes.Names)}.");
            return;
        }
        if (!PermissionModes.TryParse(argument, out PermissionMode mode))
        {
            host.Console.WriteLine($"There is no mode '{argument}'. The modes are {string.Join(", ", PermissionModes.Names)}.");
            return;
        }
        host.Policy.Mode = mode;
        host.Console.WriteLine($"The permission mode is {PermissionModes.NameOf(mode)}. {PermissionModes.Describe(mode)}");
    }

    private void Tasks()
    {
        // Read from the session by the library; no model call, and it works right after a conversation was resumed.
        TaskListSnapshot list = host.Tasks.GetTasks(host.Session());
        if (list.Items.Count == 0)
        {
            host.Console.WriteLine("The task list is empty.");
            return;
        }
        foreach (TaskItem task in list.Items)
        {
            string mark = task.Status switch
            {
                TaskItemStatus.Completed => "[x]",
                TaskItemStatus.InProgress => "[>]",
                _ => "[ ]"
            };
            host.Console.WriteLine($"  {mark} #{task.Id} {task.Subject}");
        }
    }

    private void Status()
    {
        foreach (string line in host.Status())
        {
            host.Console.WriteLine($"  {line}");
        }
        host.Console.WriteLine($"  Permission mode: {PermissionModes.NameOf(host.Policy.Mode)}. {PermissionModes.Describe(host.Policy.Mode)}");
        foreach (AllowRule rule in host.Policy.Rules)
        {
            host.Console.WriteLine($"  Allowed by the project: {rule}");
        }
        foreach (AllowRule rule in host.Policy.SessionRules)
        {
            host.Console.WriteLine($"  Allowed for this conversation: {rule}");
        }
    }

    private void Mcp()
    {
        if (host.McpServers.Count == 0)
        {
            host.Console.WriteLine($"No MCP server is connected. Name servers under \"mcpServers\" in {ProjectConfig.FileName}.");
            return;
        }
        foreach (McpToolCollection server in host.McpServers)
        {
            host.Console.WriteLine($"  {server.DisplayName}");
            foreach (AIFunction tool in server.GetAIFunctions())
            {
                // A tool the server marks read-only runs unasked; every other one is put to the user, in every mode.
                host.Console.WriteLine($"    {tool.Name}{(tool.GetKind() == ToolKind.Read ? "" : "  (asks first)")}");
            }
        }
    }

    // The conversation is not rewound, so the model is told what is back as it was, in front of the next prompt.
    private async Task UndoAsync(CancellationToken cancellationToken)
    {
        FileUndoResult undone = await host.Files.UndoAsync(host.Session(), cancellationToken).ConfigureAwait(false);
        host.Console.WriteLine(undone.ToString());
        if (!undone.NothingToUndo)
        {
            host.NoteForNextPrompt(undone.ToString());
        }
    }

    private async Task ResumeAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<SavedSession> saved = host.Store.List();
        foreach (SavedSession unreadable in saved.Where(session => !session.IsReadable))
        {
            // Listed, not offered, and left on disk.
            host.Console.WriteLine($"  Unreadable: {unreadable.FilePath} ({unreadable.Problem})");
        }
        SavedSession[] offered = [.. saved.Where(session => session.IsReadable).Take(MaxOffered)];
        if (offered.Length == 0)
        {
            host.Console.WriteLine("There is no saved conversation to go on with in this workspace.");
            return;
        }

        // The label a user picks by; the id keeps two conversations with the same first prompt apart.
        Dictionary<string, SavedSession> byLabel = offered.ToDictionary(session => $"{SessionFiles.Describe(session)}  [{session.Id[^6..]}]");
        UserQuestion which = new("Which conversation do you want to go on with?", "Resume",
            [.. byLabel.Keys.Select(label => new UserQuestionOption(label, string.Empty))]);
        string picked = (await host.Console.AskAsync([which], cancellationToken).ConfigureAwait(false))[0].SelectedLabels.FirstOrDefault() ?? string.Empty;
        if (!byLabel.TryGetValue(picked, out SavedSession? chosen))
        {
            host.Console.WriteLine("No conversation was picked.");
            return;
        }

        RestoredSession restored = await host.ResumeAsync(chosen.Id, cancellationToken).ConfigureAwait(false);
        host.Console.WriteLine($"Going on with the conversation of {SessionFiles.Describe(chosen)}. The permission mode is ask.");
        if (restored.MissingTools.Count > 0)
        {
            host.Console.WriteLine($"  Tools it was saved with that are gone: {string.Join(", ", restored.MissingTools)}.");
        }
        if (restored.NewTools.Count > 0)
        {
            host.Console.WriteLine($"  Tools that are new since it was saved: {string.Join(", ", restored.NewTools)}.");
        }
    }

    private async Task DiffAsync(CancellationToken cancellationToken)
    {
        string? diff = await GitState.DiffAsync(host.WorkspaceRoot, cancellationToken).ConfigureAwait(false);
        host.Console.WriteLine(diff switch
        {
            null => "There is no diff to show: this directory is not a git repository, or git is not installed.",
            "" => "No changes.",
            _ => diff
        });
    }

    // Plan mode: the same assistant and conversation, with changes refused by the policy and the mode in the live context.
    private async Task PlanAsync(string request, CancellationToken cancellationToken)
    {
        if (request.Length == 0)
        {
            host.Console.WriteLine("Say what to plan: /plan <request>.");
            return;
        }

        host.Policy.Mode = PermissionMode.Plan;
        await host.RunPromptAsync(request, cancellationToken).ConfigureAwait(false);
        while (!cancellationToken.IsCancellationRequested)
        {
            UserQuestion next = new("What do you want to do with this plan?", "Plan",
            [
                new UserQuestionOption(CarryOut, "Leave plan mode and let the assistant do the work, asking before each change."),
                new UserQuestionOption(Revise, "Say what to change; the assistant answers with a new plan."),
                new UserQuestionOption(Leave, "Do nothing. The mode stays plan.")
            ]);
            UserQuestionAnswer answer = (await host.Console.AskAsync([next], cancellationToken).ConfigureAwait(false))[0];
            string picked = answer.SelectedLabels.FirstOrDefault() ?? string.Empty;
            if (picked == CarryOut)
            {
                host.Policy.Mode = PermissionMode.Ask;
                await host.RunPromptAsync("Carry out the plan.", cancellationToken).ConfigureAwait(false);
                return;
            }

            // Typing an answer of one's own into the question is a revision too.
            string? change = picked == Revise
                ? await host.Console.ReadLineAsync("What should change? › ", cancellationToken).ConfigureAwait(false)
                : answer.OtherText;
            if (string.IsNullOrWhiteSpace(change))
            {
                host.Console.WriteLine($"The plan was left as it is. The permission mode is still plan; /mode {PermissionModes.Ask} leaves it.");
                return;
            }
            await host.RunPromptAsync(change, cancellationToken).ConfigureAwait(false);
        }
    }
}
