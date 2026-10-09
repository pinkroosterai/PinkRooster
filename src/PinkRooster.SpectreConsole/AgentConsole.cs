using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using PinkRooster.Agents.Eventing;
using PinkRooster.Agents.Permissions;
using PinkRooster.ToolCollections.BuiltIn.AskUserQuestion;
using Spectre.Console;

namespace PinkRooster.SpectreConsole;

/// <summary>Draws an agent's run on a Spectre.Console terminal from its events: streamed reasoning and answers, each tool call with its result, and asks the user to approve a tool call or answer a question.</summary>
/// <remarks>
/// Subscribe with <c>agent.OnEvent(console.WriteEvent)</c>. The console keeps the line it is drawing, so one console draws one run at a time:
/// two runs that overlap on the same console mix their text. A run that another run started, such as a sub-agent's, is drawn as labelled lines
/// under the tool call that started it (see <see cref="AgentConsoleOptions.ShowNestedRuns"/>). On a terminal that can render markdown (see <see cref="AnsiConsoleExtensions"/>)
/// the answer is drawn as markdown while it streams, and anything else the console draws, an event, a prompt or <see cref="WriteLine"/>, waits until
/// the answer is complete on the screen; on any other terminal the answer is written as plain indented lines as it arrives. A whole answer
/// goes through <see cref="WriteAnswer"/>, which does not stream. A sensitive value from <see cref="AgentConsoleOptions.SensitiveValues"/>
/// is drawn as <c>[redacted]</c>, also when it arrives split across two pieces of streamed text.
/// <para>
/// During a run started with <c>RunWithInputAsync</c> the console keeps an input line as the last row of the screen: it is taken
/// away before anything else is drawn and put back after, and it is off the screen while a streamed line is half drawn, the live
/// answer included. Keys typed then are kept.
/// </para>
/// </remarks>
public sealed class AgentConsole : IAgentConsole
{
    // The name of the built-in tool in PinkRooster.ToolCollections.BuiltIn, which exposes no constant for it.
    private const string AskUserQuestionTool = "AskUserQuestion";

    private readonly IAnsiConsole console;
    private readonly AgentConsoleOptions options;
    private readonly string[] sensitiveValues;
    private readonly StreamingRedactor reasoningTail;
    private readonly StreamingRedactor answerTail;
    private readonly object gate = new();
    private readonly SemaphoreSlim questionTurn = new(1, 1);
    // The keyboard has one reader at a time: a prompt for as long as it is open, else the input line's reader for one poll.
    private readonly SemaphoreSlim keyboard = new(1, 1);
    // The input line of a run that takes typed input; all read and written under the gate.
    private readonly StringBuilder typed = new();
    private readonly List<string> heldNotices = [];
    private bool inputOpen;
    private bool inputShown;
    private int openPrompts;
    // When the input line's reader last took a key, in Environment.TickCount64; a prompt waits until the keyboard has been quiet.
    private long lastKeyTicks;
    private readonly HashSet<(Guid RunId, string CallId)> backgroundStarts = [];
    private LiveAnswer? live;
    private bool midLine;
    private bool midReasoning;
    // How deep each nested run that is under way sits below the run the console draws; read and written under the gate.
    private readonly Dictionary<Guid, int> nestedDepths = [];

    /// <summary>Creates a console that draws on <paramref name="console"/>.</summary>
    /// <param name="console">The console to draw on, such as <c>AnsiConsole.Console</c>.</param>
    /// <param name="options">Colours, limits and sensitive values; null uses the defaults.</param>
    public AgentConsole(IAnsiConsole console, AgentConsoleOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(console);
        this.console = console;
        this.options = options ?? new AgentConsoleOptions();
        sensitiveValues = [.. this.options.SensitiveValues
            .Where(value => !string.IsNullOrEmpty(value))
            .Distinct(StringComparer.Ordinal)];
        reasoningTail = new StreamingRedactor(sensitiveValues, Redact);
        answerTail = new StreamingRedactor(sensitiveValues, Redact);
    }

    private string Indent => options.Indent;
    private string Accent => options.Accent;
    private string Muted => options.Muted;
    private string Success => options.Success;
    private string Failure => options.Failure;

    /// <inheritdoc />
    public bool IsInteractive => console.Profile.Capabilities.Interactive;

    /// <inheritdoc />
    public void WriteLine(string text = "")
    {
        lock (gate)
        {
            HideInput();
            EndLine();
            console.WriteLine(Redact(text));
            ShowInput();
        }
    }

    /// <inheritdoc />
    public void WriteAnswer(string text)
    {
        lock (gate)
        {
            HideInput();
            EndLine();
            DrawMarkdown(Redact(text));
            ShowInput();
        }
    }

    /// <inheritdoc />
    public void Write(string text)
    {
        lock (gate)
        {
            HideInput();
            WriteStreamed(text, reasoning: false);
            ShowInput();
        }
    }

    /// <inheritdoc />
    public void WriteEvent(AgentEvent item)
    {
        ArgumentNullException.ThrowIfNull(item);

        lock (gate)
        {
            HideInput();
            if (item.ParentRunId is null)
            {
                Draw(item);
            }
            else if (options.ShowNestedRuns)
            {
                DrawNested(item);
            }
            ShowInput();
        }
    }

    /// <inheritdoc />
    public async Task<ApprovalChoice> ConfirmToolCallAsync(ApprovalQuestion question, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);
        FunctionCallContent call = question.Call;
        if (!IsInteractive)
        {
            WriteLine($"{Indent}Skipped {call.Name}: there is no interactive terminal to approve it.");
            return ApprovalChoice.Skip;
        }

        await BeginPromptAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (gate)
            {
                EndLine();
                console.Write(new Padder(
                    new Panel(new Markup(Markup.Escape(DescribeCall(call))))
                        .Header("Allow this tool call?")
                        // As wide as the terminal, so a short call does not cut the question in the border.
                        .Expand()
                        .RoundedBorder()
                        .BorderColor(Color.FromHex(Accent))
                        .Padding(1, 0),
                    new Padding(2, 0, 0, 0)));
            }

            // The session-wide answer sits between the two it lies between, and only when the question offers it.
            string forSession = $"Allow for this session: {question.SessionRule}";
            string picked = await new SelectionPrompt<string>()
                .AddChoices(question.SessionRule is null ? ["Allow", "Skip"] : ["Allow", forSession, "Skip"])
                .UseConverter(Markup.Escape)
                .HighlightStyle(new Style(Color.FromHex(Accent), decoration: Decoration.Bold))
                .ShowAsync(console, cancellationToken).ConfigureAwait(false);
            ApprovalChoice choice = picked == "Skip" ? ApprovalChoice.Skip : picked == "Allow" ? ApprovalChoice.Allow : ApprovalChoice.AllowForSession;

            string outcome = choice switch
            {
                ApprovalChoice.Allow => "Approved.",
                ApprovalChoice.AllowForSession => $"Approved for this session: {question.SessionRule}.",
                _ => "Skipped."
            };
            console.MarkupLine($"{Indent}[{Muted}]{Markup.Escape(outcome)}[/]");
            return choice;
        }
        finally
        {
            EndPrompt();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserQuestionAnswer>> AskAsync(IReadOnlyList<UserQuestion> questions, CancellationToken cancellationToken)
    {
        if (!IsInteractive)
        {
            throw new InvalidOperationException("Answering a question needs an interactive terminal.");
        }

        // Parallel tool calls each wait their turn, so only one set of questions is open at a time.
        await questionTurn.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await BeginPromptAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await AskAllAsync(questions, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                EndPrompt();
            }
        }
        finally
        {
            questionTurn.Release();
        }
    }

    private async Task<IReadOnlyList<UserQuestionAnswer>> AskAllAsync(IReadOnlyList<UserQuestion> questions, CancellationToken cancellationToken)
    {
        List<UserQuestionAnswer> answers = [];
        for (int index = 0; index < questions.Count; index++)
        {
            string position = questions.Count > 1 ? $"[{Muted}]{index + 1} of {questions.Count}[/] " : string.Empty;
            answers.Add(await AskOneAsync(questions[index], position, cancellationToken).ConfigureAwait(false));
        }

        return answers;
    }

    /// <inheritdoc />
    public async Task<string?> ReadLineAsync(string prompt, CancellationToken cancellationToken)
    {
        if (!IsInteractive)
        {
            return null;
        }

        try
        {
            await BeginPromptAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        try
        {
            lock (gate)
            {
                EndLine();
            }

            return await console.AskOrNullAsync(prompt, cancellationToken, Accent).ConfigureAwait(false);
        }
        finally
        {
            EndPrompt();
        }
    }

    /// <summary>True when a run can take typed input here: the terminal is interactive and can erase a line.</summary>
    internal bool CanTakeInput => console.CanRenderMarkdown();

    /// <summary>
    /// Keeps the input line open and reads the keyboard until <paramref name="stop"/> is cancelled: Enter submits the line to
    /// <paramref name="submit"/>, Esc calls <paramref name="escape"/>, and <paramref name="tick"/> runs after every look at the keyboard.
    /// Returns what was typed and not submitted.
    /// </summary>
    /// <param name="submit">Takes a submitted line and returns the notice to draw for it, such as <c>queued: ...</c>.</param>
    /// <param name="escape">Called when Esc is pressed.</param>
    /// <param name="tick">Runs between looks at the keyboard, on the reader's own flow, for work that must not overlap a submit.</param>
    /// <param name="stop">Ends the reading. The keys that are waiting then are still read.</param>
    internal async Task<string> ReadInputAsync(Func<string, string> submit, Action escape, Func<Task> tick, CancellationToken stop)
    {
        lock (gate)
        {
            inputOpen = true;
            ShowInput();
        }

        try
        {
            while (!stop.IsCancellationRequested)
            {
                bool read = await ReadWaitingKeysAsync(submit, escape).ConfigureAwait(false);
                await tick().ConfigureAwait(false);
                if (!read)
                {
                    try
                    {
                        await Task.Delay(InputPollInterval, stop).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
            // A line submitted as the run ended is still taken, so the caller gets it back.
            await ReadWaitingKeysAsync(submit, escape).ConfigureAwait(false);
        }
        finally
        {
            lock (gate)
            {
                HideInput();
                inputOpen = false;
            }
        }

        lock (gate)
        {
            string draft = typed.ToString();
            typed.Clear();
            return draft;
        }
    }

    private static readonly TimeSpan InputPollInterval = TimeSpan.FromMilliseconds(20);

    // How long the keyboard must have been quiet before a prompt opens during a run that takes input.
    private const int QuietBeforePromptMilliseconds = 700;

    // Reads every key that is waiting, unless a prompt has the keyboard. Returns whether a key was read.
    private async Task<bool> ReadWaitingKeysAsync(Func<string, string> submit, Action escape)
    {
        if (!await keyboard.WaitAsync(0).ConfigureAwait(false))
        {
            return false;
        }

        bool read = false;
        try
        {
            while (console.Input.IsKeyAvailable() && console.Input.ReadKey(intercept: true) is ConsoleKeyInfo key)
            {
                read = true;
                Volatile.Write(ref lastKeyTicks, Environment.TickCount64);
                TakeKey(key, submit, escape);
            }
        }
        finally
        {
            keyboard.Release();
        }
        return read;
    }

    private void TakeKey(ConsoleKeyInfo key, Func<string, string> submit, Action escape)
    {
        string? notice = null;
        switch (key.Key)
        {
            case ConsoleKey.Enter:
                string line;
                lock (gate)
                {
                    line = typed.ToString().Trim();
                    typed.Clear();
                }
                if (line.Length > 0)
                {
                    notice = submit(line);
                }
                break;
            case ConsoleKey.Escape:
                escape();
                notice = "Esc: stopping the run.";
                break;
            case ConsoleKey.Backspace:
                lock (gate)
                {
                    if (typed.Length > 0)
                    {
                        typed.Length--;
                    }
                }
                break;
            default:
                if (!char.IsControl(key.KeyChar))
                {
                    lock (gate)
                    {
                        typed.Append(key.KeyChar);
                    }
                }
                break;
        }

        lock (gate)
        {
            HideInput();
            if (notice is not null)
            {
                string markup = $"{Indent}[{Muted}]{Markup.Escape(Redact(notice))}[/]";
                // A notice may not break into a line that is half drawn; it is drawn when that line ends.
                if (midLine || live is not null)
                {
                    heldNotices.Add(markup);
                }
                else
                {
                    console.MarkupLine(markup);
                }
            }
            ShowInput();
        }
    }

    // Takes the input line off the screen, so what is drawn next starts on an empty row. Called under the gate.
    private void HideInput()
    {
        if (inputShown)
        {
            console.Write(new ControlCode("\r\u001b[2K"));
            inputShown = false;
        }
    }

    // Draws the input line as the last row, when a run takes input, no prompt is open and no line is half drawn. Called under the gate.
    private void ShowInput()
    {
        if (!inputOpen || inputShown || openPrompts > 0 || midLine || live is not null)
        {
            return;
        }

        // One row at most, so one erase always removes it: a longer text shows its end.
        const string Mark = "› ";
        int room = Math.Max(1, console.Profile.Width - Mark.Length - 1);
        string text = Redact(typed.ToString());
        console.Markup($"[{Accent}]{Mark}[/]");
        console.Write(new Text(text.Length <= room ? text : text[^room..]));
        inputShown = true;
    }

    // A prompt takes the input line off the screen and the keyboard from its reader until EndPrompt.
    private async Task BeginPromptAsync(CancellationToken cancellationToken)
    {
        // A prompt that opens under the user's fingers would take the rest of the line they are typing, and its Enter as an answer:
        // an approval given by accident. So while a run takes input, a prompt waits until the keyboard has been quiet, and the
        // reader goes on taking the keys for the input line meanwhile.
        while (IsInputOpen() && (console.Input.IsKeyAvailable() || Environment.TickCount64 - Volatile.Read(ref lastKeyTicks) < QuietBeforePromptMilliseconds))
        {
            await Task.Delay(InputPollInterval, cancellationToken).ConfigureAwait(false);
        }

        lock (gate)
        {
            HideInput();
            openPrompts++;
        }
        try
        {
            await keyboard.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            lock (gate)
            {
                openPrompts--;
                ShowInput();
            }
            throw;
        }
    }

    private bool IsInputOpen()
    {
        lock (gate)
        {
            return inputOpen;
        }
    }

    private void EndPrompt()
    {
        keyboard.Release();
        lock (gate)
        {
            openPrompts--;
            ShowInput();
        }
    }

    // Called under the gate.
    private void Draw(AgentEvent item)
    {
        switch (item)
        {
            case StepStarted step:
                EndLine();
                console.MarkupLine($"{Indent}[{Muted}]Step: {Markup.Escape(step.Label)}[/]");
                break;
            case StepChecked check:
                EndLine();
                DrawCheck(check);
                break;
            case ReasoningDelta thought:
                WriteStreamed(thought.Text, reasoning: true);
                break;
            case ReasoningCompleted:
            case RunCompleted:
                EndLine();
                break;
            case AssistantTextDelta text:
                WriteStreamed(text.Text, reasoning: false);
                break;
            case ToolCallRequested call:
                EndLine();
                console.MarkupLine($"{Indent}[{Muted}]{Markup.Escape(call.Name)}  {Markup.Escape(Bound(Redact(DescribeArguments(call.Arguments))))}[/]");
                break;
            case ToolCallCompleted result when IsBackgroundStart(result):
                // The task's own start line said it; the call's result is only the start message.
                break;
            case ToolCallCompleted result:
                EndLine();
                WriteResult(result);
                break;
            case BackgroundTaskStarted task:
                EndLine();
                RememberBackgroundStart(task);
                console.MarkupLine($"{Indent}[{Muted}]▸ {TaskLabel(task.TaskId, task.ToolName)} started in the background[/]");
                break;
            case BackgroundTaskEnded task:
                EndLine();
                console.MarkupLine($"{Indent}{DescribeTask(task, TaskLabel(task.TaskId, task.ToolName))}");
                break;
            case BackgroundWaitStarted wait:
                EndLine();
                console.MarkupLine($"{Indent}[{Muted}]… {DescribeWait(wait)}[/]");
                break;
            case BackgroundWakeLimitReached limit:
                EndLine();
                console.MarkupLine($"{Indent}{DescribeWakeLimit(limit)}");
                break;
        }
    }

    /// <summary>
    /// Draws an event of a run that another run started, such as a sub-agent's, as whole lines under the caller's tool call: indented one
    /// level per level of nesting and labelled with the agent's name. Nothing is streamed, so runs that overlap stay readable, and the
    /// answer is left out because the caller's tool result shows it. Called under the gate.
    /// </summary>
    private void DrawNested(AgentEvent item)
    {
        if (item is RunStarted)
        {
            // ParentRunId was checked by the caller.
            nestedDepths[item.RunId] = nestedDepths.GetValueOrDefault(item.ParentRunId!.Value) + 1;
        }
        string pad = string.Concat(Enumerable.Repeat(Indent, nestedDepths.GetValueOrDefault(item.RunId, 1) + 1));
        string agent = Markup.Escape(string.IsNullOrWhiteSpace(item.AgentName) ? "sub-agent" : item.AgentName);

        switch (item)
        {
            case RunStarted:
                EndLine();
                console.MarkupLine($"{pad}[{Muted}]▸ {agent} started[/]");
                break;
            case RunCompleted run:
                EndLine();
                nestedDepths.Remove(item.RunId);
                console.MarkupLine($"{pad}{DescribeNestedRun(run, agent)}");
                break;
            case StepStarted step:
                EndLine();
                console.MarkupLine($"{pad}[{Muted}]{agent}: step {Markup.Escape(step.Label)}[/]");
                break;
            case StepChecked check:
                EndLine();
                console.MarkupLine($"{pad}[{Muted}]{agent}: check {Markup.Escape(check.Label)} {(check.Passed ? "passed" : "found problems")}[/]");
                break;
            case ReasoningCompleted thought when !string.IsNullOrWhiteSpace(thought.Text):
                EndLine();
                console.MarkupLine($"{pad}[{Muted} italic]{agent}: thinking: {Markup.Escape(Bound(Redact(thought.Text)))}[/]");
                break;
            case ToolCallRequested call:
                EndLine();
                console.MarkupLine($"{pad}[{Muted}]{agent}: {Markup.Escape(call.Name)}  {Markup.Escape(Bound(Redact(DescribeArguments(call.Arguments))))}[/]");
                break;
            case ToolCallCompleted result when IsBackgroundStart(result):
                break;
            case ToolCallCompleted result:
                EndLine();
                console.MarkupLine($"{pad}{DescribeResult(result, $"{agent}: {Markup.Escape(result.Name)}", nested: true)}");
                break;
            case BackgroundTaskStarted task:
                EndLine();
                RememberBackgroundStart(task);
                console.MarkupLine($"{pad}[{Muted}]▸ {agent}: {TaskLabel(task.TaskId, task.ToolName)} started in the background[/]");
                break;
            case BackgroundTaskEnded task:
                EndLine();
                console.MarkupLine($"{pad}{DescribeTask(task, $"{agent}: {TaskLabel(task.TaskId, task.ToolName)}")}");
                break;
            case BackgroundWaitStarted wait:
                EndLine();
                console.MarkupLine($"{pad}[{Muted}]… {agent}: {DescribeWait(wait)}[/]");
                break;
            case BackgroundWakeLimitReached limit:
                EndLine();
                console.MarkupLine($"{pad}{DescribeWakeLimit(limit, $"{agent}: ")}");
                break;
        }
    }

    // Called under the gate. The call that started a task ends at once with the start message; its result line would only repeat the task's start line.
    private void RememberBackgroundStart(BackgroundTaskStarted task)
    {
        if (task.CallId is not null)
        {
            backgroundStarts.Add((task.RunId, task.CallId));
        }
    }

    private bool IsBackgroundStart(ToolCallCompleted result) =>
        result.Status == ToolCallStatus.Succeeded && backgroundStarts.Remove((result.RunId, result.CallId));

    private static string DescribeWait(BackgroundWaitStarted wait) =>
        wait.TaskIds.Count == 1 ? "waiting for 1 background task" : $"waiting for {wait.TaskIds.Count.ToString(CultureInfo.InvariantCulture)} background tasks";

    private string DescribeWakeLimit(BackgroundWakeLimitReached limit, string label = "")
    {
        string tasks = limit.CancelledTaskIds.Count == 1 ? "1 background task" : $"{limit.CancelledTaskIds.Count.ToString(CultureInfo.InvariantCulture)} background tasks";
        return $"[{Failure}]✗[/] [{Muted}]{label}stopped waiting at the limit of {limit.Limit.ToString(CultureInfo.InvariantCulture)} wakes, {tasks} cancelled[/]";
    }

    // A task is named the way the model's own tools name it, so a line here can be matched to a WaitForTasks or GetTaskResult call.
    private static string TaskLabel(int taskId, string toolName) => $"task {taskId.ToString(CultureInfo.InvariantCulture)} ({Markup.Escape(toolName)})";

    /// <summary>The line for a background task's end as markup: its mark, <paramref name="label"/> (already escaped) and how it ended with the time.</summary>
    private string DescribeTask(BackgroundTaskEnded task, string label)
    {
        string time = Seconds(task.Duration);
        string stillRunning = task.DidNotStop ? ", its tool did not stop" : "";
        return task.State switch
        {
            BackgroundTaskState.Completed => $"[{Success}]✓[/] [{Muted}]{label}  done in {time}[/]",
            BackgroundTaskState.Failed => $"[{Failure}]✗[/] [{Muted}]{label}  failed after {time}[/]",
            BackgroundTaskState.TimedOut => $"[{Failure}]✗[/] [{Muted}]{label}  timed out after {time}{stillRunning}[/]",
            _ => $"[{Failure}]✗[/] [{Muted}]{label}  cancelled after {time}{stillRunning}[/]"
        };
    }

    private string DescribeNestedRun(RunCompleted run, string agent)
    {
        string time = Seconds(run.Duration);
        string tokens = run.Usage?.TotalTokenCount is { } total ? $", {total.ToString("N0", CultureInfo.InvariantCulture)} tokens" : "";
        return run.Outcome switch
        {
            RunOutcome.Succeeded => $"[{Muted}]◂ {agent} done in {time}{tokens}[/]",
            RunOutcome.AwaitingApproval => $"[{Muted}]◂ {agent} waits for approval[/]",
            RunOutcome.Failed => $"[{Failure}]✗[/] [{Muted}]{agent} failed ({run.Error?.GetType().Name}) after {time}{tokens}[/]",
            _ => $"[{Failure}]✗[/] [{Muted}]{agent} stopped after {time}{tokens}[/]"
        };
    }

    /// <summary>Streams answer text as markdown where the terminal can draw it, and everything else, such as reasoning, as indented lines that join across pieces; reasoning is grey italic under a <c>Thinking:</c> label.</summary>
    private void WriteStreamed(string text, bool reasoning)
    {
        if (!reasoning && console.CanRenderMarkdown())
        {
            if (midLine)
            {
                EndLine();
            }
            (live ??= new LiveAnswer(console, new StreamingRedactor(sensitiveValues, Redact))).Push(text);
            return;
        }
        if (live is not null || (midLine && midReasoning != reasoning))
        {
            EndLine();
        }
        DrawStreamed((reasoning ? reasoningTail : answerTail).Push(text), reasoning);
    }

    private void DrawStreamed(string text, bool reasoning)
    {
        string style = reasoning ? $"{Muted} italic" : "default";
        string[] lines = text.Replace("\r", "").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                console.WriteLine();
                midLine = false;
            }
            if (lines[i].Length == 0)
            {
                continue;
            }
            if (!midLine)
            {
                console.Write(new Text(Indent));
                if (reasoning && !midReasoning)
                {
                    console.Markup($"[{Muted}]Thinking:[/] ");
                }
            }
            console.Markup($"[{style}]{Markup.Escape(lines[i])}[/]");
            midLine = true;
            midReasoning = reasoning;
        }
    }

    /// <summary>The outcome on one line, then each problem to fix on its own line below it.</summary>
    private void DrawCheck(StepChecked check)
    {
        string[] problems = check.Feedback is null ? [] : [.. Redact(check.Feedback).Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries)];
        string outcome = check.Passed ? "passed" : problems.Length == 1 ? "found 1 problem" : $"found {problems.Length} problems";
        console.MarkupLine($"{Indent}[{Muted}]Check: {Markup.Escape(check.Label)} {outcome}[/]");
        foreach (string problem in problems)
        {
            console.MarkupLine($"{Indent}{Indent}[{Muted}]- {Markup.Escape(problem)}[/]");
        }
    }

    /// <summary>Draws the text as markdown on a terminal that can redraw, and as plain lines otherwise. Called under the gate.</summary>
    private void DrawMarkdown(string text) => console.WriteAnswer(text);

    /// <summary>Ends what is being streamed: the live answer is drawn to its last character, held-back text is drawn, and an open line is ended. Called under the gate.</summary>
    private void EndLine()
    {
        if (live is not null)
        {
            live.Close();
            live = null;
        }
        string rest = reasoningTail.Flush();
        if (rest.Length > 0)
        {
            DrawStreamed(rest, reasoning: true);
        }
        rest = answerTail.Flush();
        if (rest.Length > 0)
        {
            DrawStreamed(rest, reasoning: false);
        }
        if (midLine)
        {
            console.WriteLine();
            midLine = false;
        }
        midReasoning = false;
        foreach (string notice in heldNotices)
        {
            console.MarkupLine(notice);
        }
        heldNotices.Clear();
    }

    /// <summary>One header line with the outcome and time, then up to <see cref="AgentConsoleOptions.MaxResultLines"/> lines of what the tool returned.</summary>
    private void WriteResult(ToolCallCompleted result)
    {
        console.MarkupLine($"{Indent}{DescribeResult(result, Markup.Escape(result.Name), nested: false)}");
        if (result.Status != ToolCallStatus.Succeeded)
        {
            return;
        }

        string text = Redact(result.Result?.ToString() ?? "");
        string[] lines = text.Replace("\r", "").Split('\n');
        // The questions and the picks are already on screen.
        if (result.Name != AskUserQuestionTool)
        {
            WriteBody(lines);
        }
    }

    /// <summary>The header line of a tool call's result as markup: its mark, <paramref name="label"/> (already escaped) and the outcome with the time.</summary>
    private string DescribeResult(ToolCallCompleted result, string label, bool nested)
    {
        string time = Seconds(result.Duration);
        switch (result.Status)
        {
            case ToolCallStatus.Rejected:
                // A nested run's approvals are answered by its caller, which may be code and not the person at the terminal.
                string why = nested ? "not allowed" : IsInteractive ? "skipped by you" : "skipped, no interactive terminal";
                return $"[{Muted}]⊘ {label}  {why}[/]";
            case ToolCallStatus.Cancelled:
                return $"[{Failure}]✗[/] [{Muted}]{label}  cancelled after {time}[/]";
            case ToolCallStatus.Failed:
                return $"[{Failure}]✗[/] [{Muted}]{label}  failed ({result.Error?.GetType().Name}) after {time}[/]";
            default:
                return $"[{Success}]✓[/] [{Muted}]{label}  done in {time}[/]";
        }
    }

    private static string Seconds(TimeSpan duration) => $"{duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s";

    private void WriteBody(IReadOnlyList<string> lines)
    {
        string[] kept = [.. lines.Reverse().SkipWhile(string.IsNullOrWhiteSpace).Reverse()];
        foreach (string line in kept.Take(options.MaxResultLines))
        {
            console.MarkupLine($"{Indent}{Indent}[{Muted}]{Markup.Escape(BoundLine(line))}[/]");
        }
        if (kept.Length > options.MaxResultLines)
        {
            int more = kept.Length - options.MaxResultLines;
            console.MarkupLine($"{Indent}{Indent}[{Muted}]… {more} more {(more == 1 ? "line" : "lines")}[/]");
        }
    }

    // Compact JSON for an argument that is a list or an object, with quotes, angle brackets and the like left as they are:
    // the text is drawn on a terminal, never put into HTML or a script.
    private static readonly JsonSerializerOptions ReadableJson = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The arguments on one line, as <c>name: value, name: value</c>. Text is drawn as the text it is, not as a JSON string, so a quote is a quote and a line break is a break.</summary>
    private static string DescribeArguments(IEnumerable<KeyValuePair<string, object?>>? arguments) =>
        arguments is null ? "" : string.Join(", ", arguments.Select(argument => $"{argument.Key}: {ValueText(argument.Value)}"));

    /// <summary>
    /// The call for the approval panel, where the user decides: the tool's name, then one argument per row. A text of several lines
    /// keeps its lines, indented under its name, so a script reads as a script; past <see cref="AgentConsoleOptions.MaxApprovalLines"/>
    /// lines the rest is counted.
    /// </summary>
    private string DescribeCall(FunctionCallContent call)
    {
        List<string> rows = [call.Name];
        int left = options.MaxApprovalLines;
        foreach (KeyValuePair<string, object?> argument in call.Arguments ?? new Dictionary<string, object?>())
        {
            string[] lines = Redact(ValueText(argument.Value)).Replace("\r", "").Split('\n');
            if (lines.Length == 1)
            {
                rows.Add($"{argument.Key}: {lines[0]}");
                left--;
                continue;
            }

            rows.Add($"{argument.Key}:");
            int shown = Math.Clamp(left, 1, lines.Length);
            rows.AddRange(lines.Take(shown).Select(line => $"{Indent}{line}"));
            if (shown < lines.Length)
            {
                int more = lines.Length - shown;
                rows.Add($"{Indent}… {more} more {(more == 1 ? "line" : "lines")}");
            }
            left -= shown;
        }
        return string.Join("\n", rows);
    }

    private static string ValueText(object? value) => value switch
    {
        null => "null",
        string text => text,
        bool flag => flag ? "true" : "false",
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString() ?? "",
        JsonElement element => JsonSerializer.Serialize(element, ReadableJson),
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => JsonSerializer.Serialize(value, ReadableJson)
    };

    private string Bound(string value)
    {
        string singleLine = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return singleLine.Length <= options.MaxLineLength ? singleLine : $"{singleLine[..options.MaxLineLength]}…";
    }

    private string BoundLine(string line)
    {
        string trimmed = line.TrimEnd();
        return trimmed.Length <= options.MaxLineLength ? trimmed : $"{trimmed[..options.MaxLineLength]}…";
    }

    private string Redact(string text)
    {
        foreach (string sensitiveValue in sensitiveValues)
        {
            text = text.Replace(sensitiveValue, "[redacted]", StringComparison.Ordinal);
        }
        return text;
    }

    private async Task<UserQuestionAnswer> AskOneAsync(UserQuestion question, string position, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            EndLine();
            console.MarkupLine($"{Indent}{position}[bold]{Markup.Escape(question.Question)}[/] [{Muted}]{Markup.Escape(question.Header)}[/]");
        }

        Choice[] choices = [.. question.Options.Select(option => new Choice(option.Label, option.Description)), Choice.Other];
        Style highlight = new(Color.FromHex(Accent), decoration: Decoration.Bold);
        IReadOnlyList<Choice> picked;
        if (question.MultiSelect)
        {
            picked = await new MultiSelectionPrompt<Choice>()
                .AddChoices(choices)
                .UseConverter(FormatChoice)
                .HighlightStyle(highlight)
                .InstructionsText($"[{Muted}](Space to pick, Enter to confirm)[/]")
                .ShowAsync(console, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            picked = [await new SelectionPrompt<Choice>()
                .AddChoices(choices)
                .UseConverter(FormatChoice)
                .HighlightStyle(highlight)
                .ShowAsync(console, cancellationToken).ConfigureAwait(false)];
        }

        string? otherText = null;
        if (picked.Contains(Choice.Other))
        {
            otherText = await console.PromptAsync(new TextPrompt<string>($"{Indent}[{Accent}]Your answer ›[/]"), cancellationToken).ConfigureAwait(false);
        }

        string[] labels = [.. picked.Where(choice => choice != Choice.Other).Select(choice => choice.Label)];
        string echo = string.Join(", ", otherText is null ? labels : [.. labels, otherText]);
        console.MarkupLine($"{Indent}[{Muted}]You picked:[/] {Markup.Escape(echo)}");
        return new UserQuestionAnswer(labels, otherText);
    }

    private string FormatChoice(Choice choice) => choice == Choice.Other
        ? $"Other [{Muted}](type your own answer)[/]"
        : $"{Markup.Escape(choice.Label)} [{Muted}]{Markup.Escape(choice.Description)}[/]";

    private sealed record Choice(string Label, string Description)
    {
        public static readonly Choice Other = new(string.Empty, string.Empty);
    }
}
