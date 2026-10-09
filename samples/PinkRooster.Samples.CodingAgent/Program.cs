using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents.Eventing;
using PinkRooster.Agents.Permissions;
using PinkRooster.Agents.Sessions;
using PinkRooster.Agents.SubAgents;
using PinkRooster.Samples.CodingAgent.Commands;
using PinkRooster.Samples.CodingAgent.Helpers;
using PinkRooster.Samples.CodingAgent.History;
using PinkRooster.Samples.CodingAgent.Images;
using PinkRooster.Samples.CodingAgent.Mcp;
using PinkRooster.Samples.CodingAgent.ModelSettings;
using PinkRooster.Samples.CodingAgent.Permissions;
using PinkRooster.Samples.CodingAgent.Project;
using PinkRooster.Samples.CodingAgent.Sessions;
using PinkRooster.Samples.CodingAgent.Skills;
using PinkRooster.Samples.CodingAgent.Status;
using PinkRooster.SpectreConsole;
using PinkRooster.ToolCollections;
using PinkRooster.ToolCollections.BuiltIn;
using PinkRooster.ToolCollections.BuiltIn.AskUserQuestion;
using PinkRooster.ToolCollections.BuiltIn.DateTimes;
using PinkRooster.ToolCollections.BuiltIn.FileOperations;
using PinkRooster.ToolCollections.BuiltIn.Images;
using PinkRooster.ToolCollections.BuiltIn.Shells;
using PinkRooster.ToolCollections.BuiltIn.TaskLists;
using PinkRooster.ToolCollections.Mcp;
using Spectre.Console;

namespace PinkRooster.Samples.CodingAgent;

/// <summary>Composes the coding assistant from its pieces and runs the chat loop.</summary>
public static class Program
{
    /// <summary>Exit code for a program ended with Ctrl+C at the prompt, the shell convention.</summary>
    public const int Interrupted = 130;

    public static async Task<int> Main()
    {
        using Interrupt interrupt = new();
        Console.CancelKeyPress += (_, pressed) =>
        {
            pressed.Cancel = true;
            interrupt.Raise();
        };

        return await RunAsync(
            new HostSetup
            {
                WorkspaceRoot = Directory.GetCurrentDirectory(),
                ModelSettingsPath = ModelSettingsFile.DefaultPath,
                CreateConsole = secrets => new AgentConsole(AnsiConsole.Console, new AgentConsoleOptions { SensitiveValues = secrets }),
                // Library documentation over the network, unless the workspace's config file names its own servers.
                DefaultMcpServers = [McpServers.Context7],
                Interrupt = interrupt
            },
            CancellationToken.None);
    }

    /// <summary>The whole program, with everything it takes from outside given: the tests call this.</summary>
    /// <returns>0 when the user ended it, 1 when its setup or a model call failed, 130 when Ctrl+C ended it at the prompt.</returns>
    public static async Task<int> RunAsync(HostSetup setup, CancellationToken cancellationToken)
    {
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, setup.Interrupt.ProgramToken);

        // What cannot be drawn before there is a screen goes to the plain console: a broken settings or config file.
        IReadOnlyList<ModelEntry> models;
        ProjectConfig config;
        bool settingsCreated;
        try
        {
            models = ModelSettingsFile.Load(setup.ModelSettingsPath, out settingsCreated);
            config = ProjectConfig.Load(setup.WorkspaceRoot);
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
        {
            setup.CreateConsole([]).WriteLine(error.Message);
            return 1;
        }

        IAgentConsole console = setup.CreateConsole([.. models.Select(model => model.ApiKey).OfType<string>()]);
        if (!console.IsInteractive)
        {
            console.WriteLine("The coding assistant asks before it changes anything, so it needs an interactive terminal.");
            return 1;
        }
        if (settingsCreated)
        {
            console.WriteLine($"Created {setup.ModelSettingsPath} with Ollama's {ModelEntry.DefaultModel}. Edit it to use another model.");
        }

        try
        {
            ModelEntry model = await PickModelAsync(models, console, stop.Token);

            // The tools. One workspace is shared by the file tools and the shell, so both are held to the same directory.
            Workspace workspace = new(setup.WorkspaceRoot);
            FileOperationsToolCollection files = new FileOperationsToolCollectionBuilder().InWorkspace(workspace).KeepBackups().Build();
            ShellToolCollection shell = new(workspace);
            TaskListToolCollection tasks = new TaskListToolCollectionBuilder().PerSession().Build();
            AskUserQuestionToolCollection questions = new(console.AskAsync);
            DateTimeToolCollection clock = new();

            // The extensions: MCP servers of the config file, each a tool collection, and the workspace's skills.
            IReadOnlyList<McpToolCollection> mcpServers = await McpServers.ConnectAllAsync(
                config.McpServers ?? setup.DefaultMcpServers, setup.ConnectMcpAsync, console.WriteLine, stop.Token);
            AgentSkillsProvider? skills = WorkspaceSkills.Find(workspace.RootDirectory, out int skillCount);

            // One policy answers every approval: the assistant's and its helpers'.
            PermissionPolicy policy = PermissionSetup.Create(console.ConfirmToolCallAsync, config, [files, shell, tasks, questions, clock, .. mcpServers]);
            IChatClient client = setup.CreateClient(model);
            SubAgentToolCollection helpers = HelperTools.Create(
                [.. models.Select(entry => (entry, entry == model ? client : setup.CreateClient(entry)))], workspace, shell, policy);
            policy.WithTools(helpers);

            // Looking at images: a model the settings mark as able to see answers questions about image files, as text.
            ModelEntry? visionModel = ImageTools.PickModel(models, model);
            ImageToolCollection? images = visionModel is null ? null : ImageTools.Create(workspace, visionModel == model ? client : setup.CreateClient(visionModel));
            if (images is not null)
            {
                policy.WithTools(images);
            }

            // A long conversation is shortened on its way to the model; the model itself writes the summary.
            Compaction compaction = new(client, model.ContextSize, model.CompactAt, console.WriteLine);
            SessionStore store = SessionFiles.For(workspace.RootDirectory);
            UsageMeter usage = new();

            int toolCalls = 0;
            int prompts = 0;
            // What the model must be told in front of the next prompt, such as what /undo put back.
            string? note = null;
            CodingAssistant assistant = await NewAssistantAsync();
            AgentSession session = await assistant.CreateSessionAsync(stop.Token);

            console.WriteLine($"Coding assistant on {model.Model}, in {workspace.RootDirectory}.");
            if (mcpServers.Count > 0 || skillCount > 0)
            {
                console.WriteLine($"MCP servers: {(mcpServers.Count == 0 ? "none" : string.Join(", ", mcpServers.Select(server => server.DisplayName)))}. Skills: {skillCount}.");
            }
            if (visionModel is not null)
            {
                console.WriteLine($"Image tool: on {visionModel.Model}.");
            }
            console.WriteLine($"Permission mode: {PermissionModes.NameOf(policy.Mode)}. Type a request, /help for the commands, /exit to leave.");
            console.WriteLine();

            // A line typed during a run that the model did not read comes back from the run and is handled before the next prompt.
            Queue<string> carried = new();
            SlashCommands commands = new(new CommandHost
            {
                Console = console,
                Policy = policy,
                Tasks = tasks,
                Files = files,
                Store = store,
                Compaction = compaction,
                McpServers = mcpServers,
                WorkspaceRoot = workspace.RootDirectory,
                Session = () => session,
                RunPromptAsync = RunPromptAsync,
                ClearAsync = ClearAsync,
                ResumeAsync = ResumeAsync,
                NoteForNextPrompt = text => note = text,
                Status = () =>
                [
                    $"Model: {model.Model} on {model.Endpoint.Host}" + (compaction.TriggerTokens is int at ? $", compacted past {at:N0} tokens" : ", compacted on request only"),
                    $"Tokens: {usage.RunTokens:N0} in the last run, {usage.SessionTokens:N0} in this conversation",
                    $"Turn limit: {config.MaxTurns}. Verify command: {config.VerifyCommand ?? "none; the assistant finds the build and tests itself"}",
                    $"MCP servers: {mcpServers.Count}. Skills: {skillCount}. Saved conversations: {store.Directory}",
                    visionModel is null
                        ? $"Image tool: off. Mark a model \"vision\": true in {setup.ModelSettingsPath} to let the assistant look at images"
                        : $"Image tool: on {visionModel.Model}"
                ]
            });

            while (true)
            {
                string? line = carried.Count > 0 ? carried.Dequeue() : await console.ReadLineAsync("You › ", stop.Token);
                if (line is null)
                {
                    break;
                }
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }
                if (SlashCommands.IsCommand(line))
                {
                    if (!await commands.RunAsync(line, stop.Token))
                    {
                        break;
                    }
                    console.WriteLine();
                    continue;
                }
                await RunPromptAsync(line, stop.Token);
            }

            await assistant.DisposeAsync();
            foreach (McpToolCollection server in mcpServers)
            {
                await server.DisposeAsync();
            }
            if (prompts > 0 && Volatile.Read(ref toolCalls) == 0)
            {
                // The model, not the assistant, is the likely cause: a model without tool support answers in text and never calls one.
                console.WriteLine($"The model called no tool in this session. If it should have, use a model that supports tool calls: {model.Model} may not.");
            }
            return stop.IsCancellationRequested ? Interrupted : 0;

            // The assistant, built from the pieces above and the instruction file as it is now.
            async Task<CodingAssistant> NewAssistantAsync()
            {
                string? instructions = ProjectInstructions.Read(workspace.RootDirectory);
                if (instructions is not null)
                {
                    console.WriteLine($"Read {ProjectInstructions.FileName}: {instructions.Length:N0} characters, sent with every request.");
                }
                CodingAssistant built = new(client, workspace, [files, shell, questions, tasks, clock, helpers, .. mcpServers, .. images is null ? [] : new ToolCollection[] { images }], files, policy, config, instructions, compaction, skills);
                built.OnEvent(console.WriteEvent);
                built.OnEvent<ToolCallStarted>(_ => Interlocked.Increment(ref toolCalls));
                // The status line's numbers come from the events: every model call reports its usage.
                built.OnEvent<ModelCallCompleted>(usage.Count);
                // Built here, so a mistake in the composition surfaces at start and not at the first prompt.
                await built.InitializeAsync(stop.Token);
                return built;
            }

            // One user message: streamed to the console, with approvals answered by the policy and typed lines taken meanwhile.
            async Task RunPromptAsync(string prompt, CancellationToken token)
            {
                prompts++;
                usage.BeginRun();
                // Everything the file tools change from here on is one unit for undo.
                files.BeginUndoUnit(session);
                string sent = note is null ? prompt : $"{note}\n\n{prompt}";
                note = null;
                using CancellationTokenSource run = setup.Interrupt.BeginRun(token);
                try
                {
                    ConsoleRunResult result = await assistant.RunWithInputAsync(
                        sent,
                        console,
                        session,
                        new ConsoleRunOptions { Policy = policy, HoldLine = SlashCommands.IsCommand },
                        run.Token);
                    foreach (string unread in result.UnreadLines)
                    {
                        carried.Enqueue(unread);
                    }
                    if (result.Cancelled)
                    {
                        console.WriteLine("Stopped. The conversation is as it stands.");
                    }
                }
                catch (OperationCanceledException) when (run.IsCancellationRequested && !token.IsCancellationRequested)
                {
                    // Ctrl+C during a run stops the run; at the prompt it ends the program.
                    console.WriteLine("Stopped. The conversation is as it stands.");
                }
                finally
                {
                    setup.Interrupt.EndRun();
                }

                // Saved after every run, so the file is the conversation as it stands; a stopped run is saved too.
                try
                {
                    await store.SaveAsync(assistant, session, prompt, token);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    console.WriteLine($"The conversation could not be saved to {store.Directory}: {error.Message}");
                }
                console.WriteLine(usage.StatusLine(model.Model, PermissionModes.NameOf(policy.Mode)));
                console.WriteLine();
            }

            // A new conversation: a new session, the instruction file read again, and the mode and the prompt's rules as at start.
            async Task ClearAsync(CancellationToken token)
            {
                await assistant.DisposeAsync();
                assistant = await NewAssistantAsync();
                session = await assistant.CreateSessionAsync(token);
                policy.Reset();
                usage.BeginSession();
                note = null;
            }

            // A saved conversation goes on: its history, its task list and its undo record come back with the session. The mode
            // and the rules added at a prompt belonged to the conversation that was on screen, so they start over.
            async Task<RestoredSession> ResumeAsync(string id, CancellationToken token)
            {
                RestoredSession restored = await store.RestoreAsync(assistant, id, token);
                session = restored.Session;
                policy.Reset();
                usage.BeginSession();
                note = null;
                return restored;
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            return Interrupted;
        }
        catch (Exception error)
        {
            console.WriteLine($"The assistant stopped on an error: {error.Message}");
            console.WriteLine($"If the model could not be reached, check its entry in {setup.ModelSettingsPath}.");
            return 1;
        }
    }

    // With several models in the settings, the user picks the one the assistant itself runs on; the others stay available to helpers.
    private static async Task<ModelEntry> PickModelAsync(IReadOnlyList<ModelEntry> models, IAgentConsole console, CancellationToken cancellationToken)
    {
        if (models.Count == 1)
        {
            return models[0];
        }

        UserQuestion question = new("Which model should the assistant run on?", "Model",
            [.. models.Select(model => new UserQuestionOption(model.Model, $"on {model.Endpoint.Host}. {model.UseWhen}"))]);
        UserQuestionAnswer answer = (await console.AskAsync([question], cancellationToken))[0];
        string picked = answer.SelectedLabels.FirstOrDefault() ?? answer.OtherText ?? string.Empty;
        return models.FirstOrDefault(model => string.Equals(model.Model, picked.Trim(), StringComparison.OrdinalIgnoreCase)) ?? models[0];
    }
}
