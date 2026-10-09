using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents;
using PinkRooster.Agents.Eventing;
using PinkRooster.Agents.Permissions;
using PinkRooster.Agents.Steps;
using PinkRooster.Samples.CodingAgent.History;
using PinkRooster.Samples.CodingAgent.Permissions;
using PinkRooster.Samples.CodingAgent.Project;
using PinkRooster.ToolCollections;
using PinkRooster.ToolCollections.BuiltIn;
using PinkRooster.ToolCollections.BuiltIn.FileOperations;

namespace PinkRooster.Samples.CodingAgent;

/// <summary>
/// The coding assistant as an agent class. Its brief is the attributes; what needs a constructor argument is added in
/// <see cref="Configure"/>; what changes during a conversation is sent by <see cref="GetContextAsync"/>; and its one step of its own,
/// checking the work after a change, is <see cref="NextAsync"/>.
/// </summary>
[AgentRole("You are a coding assistant that works in the user's repository from a terminal: you read code, change it, run it and report what you did.")]
[AgentObjective("The user's request is done in the repository, checked by a build or a test run where the project has one, and the user knows what changed.")]
[AgentInstruction(
    "Find code with SearchFiles and FindFiles and read it with ReadFile before you change it; read a long file in sections.",
    "For work of more than two steps, write the steps into the task list first and keep it current as you go. When you are asked to carry out a plan, start by writing its steps there.",
    "Send a wide search or a review to a helper with RunSubAgent, giving it the read tool set, and keep your own context for the change. Run it in the background when you have other work meanwhile.",
    "Start a long build or test run in the background and go on with what does not depend on it.",
    "When a request can be read in more than one way and the choice matters, ask with AskUserQuestion.")]
[AgentConstraint(
    "Never state what a file contains, or that a command passed, without having read it or run it in this conversation.",
    "Change only what the request needs; leave unrelated code as it is.",
    "When a tool call is refused, do not look for another way to do the same thing: go on without it, or say what you need.")]
[AgentOutputFormat(
    "After you changed files, end your answer with a walkthrough: each changed file with what changed in it in one line, then how to check the change, as the command to run or what to look at. " +
    "When you changed nothing, answer plainly, without a walkthrough.")]
// Every tool that changes something asks first; WriteFile and DeleteFile already do so on their collection.
[AgentRequireApproval("RunShell", "CreateFile", "EditFile", "MoveFile")]
// A long command and a helper may run while the assistant goes on; this also gives the agent the inbox that typing during a run posts to.
[AgentAllowBackground("RunShell", "RunSubAgent")]
public sealed class CodingAssistant(
    IChatClient chatClient,
    Workspace workspace,
    IReadOnlyList<ToolCollection> tools,
    FileOperationsToolCollection files,
    PermissionPolicy policy,
    ProjectConfig config,
    string? projectInstructions,
    Compaction compaction,
    AIContextProvider? skills) : SteppedAgent(chatClient)
{
    /// <summary>The prompt <c>/init</c> sends. A run that starts with it writes the instruction file, not code, so it gets no verify step.</summary>
    public const string InitPrompt =
        $"Explore this repository and write {ProjectInstructions.FileName} in its root: what the project is, how it is laid out, the commands that build and test it, " +
        "and the conventions a contributor has to follow. Keep it short and specific to this repository; leave out what any developer would assume. " +
        "If the file exists, improve it instead of replacing it.";

    private const string ChangedKey = "PinkRooster.Samples.CodingAgent.FilesChanged";

    // The tools whose success means a file changed: those of the file collection that declare themselves edits.
    private readonly HashSet<string> editTools = [.. files.GetAIFunctions().Where(tool => tool.GetKind() == ToolKind.Edit).Select(tool => tool.Name)];

    // What a message's run has to remember lives in the session, never in a field: one instance serves every session.
    private sealed record Message(bool IsInit);

    private sealed record FilesChanged;

    /// <summary>The most turns one user message takes: the first, and a verify turn after each turn that changed files.</summary>
    protected override int MaxTurns => config.MaxTurns;

    protected override void Configure(AgentBuilder agent)
    {
        agent.WithName(nameof(CodingAssistant))
            .WithTools(tools)
            // Below MAF's history, so a long conversation is shortened on its way to the model and saved whole.
            .ConfigureClient(client => client.Use(inner => new CompactingChatClient(inner, compaction)));
        if (skills is not null)
        {
            // The workspace's skills: MAF's provider lists them in the system prompt and loads one when the model asks.
            agent.WithContextProvider(skills);
        }
        if (projectInstructions is not null)
        {
            // The project's own instruction file, whole, as standing background. It is read at start and at /clear.
            agent.WithBackground($"The project's own instructions, from {ProjectInstructions.FileName} in the workspace root:\n\n{projectInstructions}");
        }
    }

    /// <summary>Sent before every model call and never stored: the permission mode, which the user can change between messages, and the git state.</summary>
    protected override async ValueTask<string?> GetContextAsync(CancellationToken cancellationToken)
    {
        PermissionMode mode = policy.Mode;
        string context = $"## Session\nPermission mode: {PermissionModes.NameOf(mode)}. {PermissionModes.Describe(mode)}";
        string? git = await GitState.DescribeAsync(workspace.RootDirectory, cancellationToken).ConfigureAwait(false);
        return git is null ? context : $"{context}\nGit: {git}";
    }

    /// <summary>Marks, in the session of the run in progress, that one of the assistant's own file tools changed a file.</summary>
    protected override void OnEvent(AgentEvent item)
    {
        // A helper's calls have a parent run; a refused change comes back as text that starts with "Error:".
        if (item is ToolCallCompleted { ParentRunId: null, Status: ToolCallStatus.Succeeded } call
            && editTools.Contains(call.Name)
            && !(call.Result?.ToString() ?? string.Empty).StartsWith("Error:", StringComparison.Ordinal)
            && AIAgent.CurrentRunContext?.Session is AgentSession session)
        {
            session.StateBag.SetValue(ChangedKey, new FilesChanged());
        }
    }

    protected override ValueTask StartAsync(StepContext step, CancellationToken cancellationToken)
    {
        step.Session.StateBag.TryRemoveValue(ChangedKey);
        step.SetState(new Message(IsInit: step.Request == InitPrompt));
        return ValueTask.CompletedTask;
    }

    /// <summary>After a turn that changed files, the assistant checks its work; a turn that changed none ends the run.</summary>
    protected override ValueTask<NextStep> NextAsync(StepContext step, CancellationToken cancellationToken)
    {
        bool changed = step.Session.StateBag.TryRemoveValue(ChangedKey);
        if (!changed || policy.Mode == PermissionMode.Plan || step.GetState<Message>().IsInit)
        {
            return ValueTask.FromResult(NextStep.Stop());
        }

        string check = config.VerifyCommand is string command
            ? $"Run `{command}` with RunShell."
            : "Find how this project is built and tested, and run that with RunShell.";
        return ValueTask.FromResult(NextStep.Send(
            "verify",
            $"You changed files. {check} Fix what fails. When it passes, or when you cannot fix it, end with the walkthrough and say plainly what still fails."));
    }
}
