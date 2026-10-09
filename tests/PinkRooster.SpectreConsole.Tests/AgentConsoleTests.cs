using Microsoft.Extensions.AI;
using PinkRooster.Agents.Eventing;
using PinkRooster.Agents.Permissions;
using Spectre.Console.Testing;

namespace PinkRooster.SpectreConsole.Tests;

public sealed class AgentConsoleTests
{
    [Fact]
    public void StreamedTextIsIndentedAndJoinedAcrossDeltas()
    {
        TestConsole console = new();

        Draw(new AgentConsole(console), new AssistantTextDelta("It builds "), new AssistantTextDelta("two libraries.\nAnd tests."));

        Assert.Contains("  It builds two libraries.\n  And tests.", Normalize(console.Output));
    }

    [Fact]
    public void Markdown_IsFormatted_OnATerminalThatCanRedraw()
    {
        TestConsole console = new TestConsole().EmitAnsiSequences().Interactive();

        new AgentConsole(console).WriteAnswer("Some **bold** text.\n\n| A | B |\n|---|---|\n| 1 | 2 |");

        string output = Normalize(console.Output);
        Assert.DoesNotContain("**", output);
        Assert.DoesNotContain("|---|", output);
        Assert.EndsWith("\n", output);
    }

    [Fact]
    public void StreamedAnswer_SplitInsideBoldAndATableRow_IsDrawnAsMarkdown_AndNothingIsWrittenAfterTheCall()
    {
        TestConsole console = new TestConsole().EmitAnsiSequences().Interactive();
        AgentConsole sample = new(console);

        Draw(sample,
            new AssistantTextDelta("Some **bo"), new AssistantTextDelta("ld** text.\n\n| Name | Qua"), new AssistantTextDelta("ntity |\n|---|---|\n| tea "),
            new AssistantTextDelta("| 2 |\n"), new RunCompleted(RunOutcome.Succeeded));

        string output = Plain(console.Output);
        Assert.Contains("Some bold text.", output);
        Assert.DoesNotContain("**", output);
        Assert.DoesNotContain("|---|", output);
        Assert.All(new[] { "Name", "Quantity", "tea", "2" }, word => Assert.Contains(word, output));
        Thread.Sleep(100);
        Assert.Equal(output, Plain(console.Output));
    }

    [Fact]
    public void StreamedAnswer_IsDrawnBeforeAToolCallAndAfterItsText_InOrder()
    {
        TestConsole console = new TestConsole().EmitAnsiSequences().Interactive();
        AgentConsole sample = new(console);

        Draw(sample, new AssistantTextDelta("First text"), new ToolCallRequested("c1", "Search", Arguments(("query", "tea"))),
            Completed("c1", "Search", "found"), new AssistantTextDelta("Second text"), new RunCompleted(RunOutcome.Succeeded));

        string output = Plain(console.Output);
        int first = output.IndexOf("First text", StringComparison.Ordinal);
        int tool = output.IndexOf("Search", StringComparison.Ordinal);
        int second = output.IndexOf("Second text", StringComparison.Ordinal);
        Assert.InRange(first, 0, tool - 1);
        Assert.InRange(tool, first + 1, second - 1);
    }

    [Fact]
    public async Task ConfirmToolCallAsync_DuringALiveAnswer_DrawsItsPanelAfterTheAnswer()
    {
        TestConsole console = new TestConsole().EmitAnsiSequences().Interactive();
        console.Input.PushKey(ConsoleKey.Enter);
        AgentConsole sample = new(console);

        Draw(sample, new AssistantTextDelta("About to search the web."));
        bool approved = await sample.ConfirmToolCallAsync(Call("c1", "Search", ("query", "tea")), TestContext.Current.CancellationToken);

        string output = Plain(console.Output);
        Assert.True(approved);
        Assert.InRange(output.IndexOf("About to search the web.", StringComparison.Ordinal), 0, output.IndexOf("Allow this tool call?", StringComparison.Ordinal) - 1);
    }

    [Fact]
    public void StreamedHeadingAndLink_SplitAcrossDeltas_ReadAsWriteAnswerDrawsThem()
    {
        TestConsole streamed = new TestConsole().EmitAnsiSequences().Interactive();
        AgentConsole sample = new(streamed);
        Draw(sample, new AssistantTextDelta("# Ti"), new AssistantTextDelta("tle\n\nSee [the doc"), new AssistantTextDelta("s](http://x.y) now.\n"), new RunCompleted(RunOutcome.Succeeded));
        TestConsole whole = new TestConsole().EmitAnsiSequences().Interactive();

        new AgentConsole(whole).WriteAnswer("# Title\n\nSee [the docs](http://x.y) now.\n");

        foreach (TestConsole console in new[] { streamed, whole })
        {
            string output = Plain(console.Output);
            Assert.Contains("See the docs (http://x.y) now.", output);
            Assert.DoesNotContain("**", output);
            Assert.DoesNotContain("[](", output);
        }
        Assert.Equal(Plain(whole.Output).Trim(), Plain(streamed.Output).Trim());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StreamedSensitiveValue_SplitAcrossDeltas_IsRedacted(bool ansi)
    {
        TestConsole console = ansi ? new TestConsole().EmitAnsiSequences().Interactive() : new TestConsole();
        AgentConsole sample = new(console, new AgentConsoleOptions { SensitiveValues = ["sk-secret-key"] });

        Draw(sample, new AssistantTextDelta("The key is sk-sec"), new AssistantTextDelta("ret-key, keep it."), new RunCompleted(RunOutcome.Succeeded));

        string output = Plain(console.Output);
        Assert.Contains("The key is [redacted], keep it.", output);
        Assert.DoesNotContain("sk-", output);
        Assert.DoesNotContain("secret", output);
    }

    [Fact]
    public void StreamedReasoning_WithASensitiveValueSplitAcrossDeltas_IsRedacted()
    {
        TestConsole console = new TestConsole().EmitAnsiSequences().Interactive();
        AgentConsole sample = new(console, new AgentConsoleOptions { SensitiveValues = ["hunter2"] });

        Draw(sample, new ReasoningDelta("password hun"), new ReasoningDelta("ter2 is weak"), new ReasoningCompleted("password hunter2 is weak"));

        string output = Plain(console.Output);
        Assert.Contains("password [redacted] is weak", output);
        Assert.DoesNotContain("hunter", output);
    }

    [Fact]
    public void StreamedAnswer_OnAConsoleWithoutAnsi_IsWrittenAsPlainLinesAsItArrives()
    {
        TestConsole console = new();
        AgentConsole sample = new(console);

        sample.WriteEvent(new AssistantTextDelta("Some **bold** "));
        Assert.Equal("  Some **bold** ", Normalize(console.Output));
        sample.WriteEvent(new AssistantTextDelta("text.\nSecond line."));
        Assert.Equal("  Some **bold** text.\n  Second line.", Normalize(console.Output));

        sample.WriteEvent(new RunCompleted(RunOutcome.Succeeded));
        Assert.Equal("  Some **bold** text.\n  Second line.\n", Normalize(console.Output));
    }

    [Fact]
    public void StreamedAnswer_OfBlankText_DrawsNothing()
    {
        TestConsole console = new TestConsole().EmitAnsiSequences().Interactive();

        Draw(new AgentConsole(console), new AssistantTextDelta("  \n"), new RunCompleted(RunOutcome.Succeeded));

        Assert.Equal("", console.Output);
    }

    [Fact]
    public void Markdown_IsWrittenAsPlainLines_WhenTheTerminalCannotRedraw()
    {
        TestConsole console = new();

        new AgentConsole(console).WriteAnswer("Some **bold** text.");

        Assert.Equal("Some **bold** text.\n", Normalize(console.Output));
    }

    [Fact]
    public void ReasoningStreamsInFull_UnderAThinkingLabel_BeforeTheAnswer()
    {
        TestConsole console = new();

        Draw(new AgentConsole(console),
            new ReasoningDelta("The user wants "),
            new ReasoningDelta("[a list].\nKeep it short."),
            new ReasoningCompleted("The user wants [a list].\nKeep it short."),
            new AssistantTextDelta("Here it is."));

        Assert.Contains("  Thinking: The user wants [a list].\n  Keep it short.\n  Here it is.", Normalize(console.Output));
    }

    [Fact]
    public void ToolCall_ShowsItsArguments_ThenItsOutcomeTimeAndOutput()
    {
        TestConsole console = new();

        Draw(new AgentConsole(console),
            new ToolCallRequested("c1", "GetTicket", Arguments(("number", "PR-7"))),
            Completed("c1", "GetTicket", "PR-7: Login fails on Safari.\nState: open."));

        string output = Normalize(console.Output);
        Assert.Contains("  GetTicket  number: PR-7\n  ✓ GetTicket  done in 0.8 s\n    PR-7: Login fails on Safari.\n    State: open.\n", output);
    }

    [Fact]
    public void LongResult_ShowsItsFirstTenLines_ThenHowManyMore()
    {
        TestConsole console = new();

        Draw(new AgentConsole(console), Completed("c1", "Lookup", string.Join("\n", Enumerable.Range(1, 25).Select(line => $"line {line}"))));

        string output = Normalize(console.Output);
        Assert.Contains("    line 10\n    … 15 more lines\n", output);
        Assert.DoesNotContain("line 11", output);
    }

    [Fact]
    public void RefusedCall_IsDrawnAsSkipped_NeverAsSucceeded()
    {
        TestConsole console = new TestConsole().Interactive();

        Draw(new AgentConsole(console), new ToolCallCompleted("c1", "CloseTicket", ToolCallStatus.Rejected, "The user did not allow this.", null, TimeSpan.Zero));

        string output = Normalize(console.Output);
        Assert.Contains("  ⊘ CloseTicket  skipped by you", output);
        Assert.DoesNotContain("✓", output);
    }

    [Fact]
    public void FailedCallsAndErrorResults_AreMarked()
    {
        TestConsole console = new();

        Draw(new AgentConsole(console),
            new ToolCallCompleted("c1", "Lookup", ToolCallStatus.Failed, null, new InvalidOperationException("boom"), TimeSpan.FromSeconds(1.2)));

        Assert.Contains("  ✗ Lookup  failed (InvalidOperationException) after 1.2 s", Normalize(console.Output));
    }

    [Fact]
    public void EventsAreRedacted()
    {
        TestConsole console = new();

        Draw(new AgentConsole(console, new AgentConsoleOptions { SensitiveValues = ["secret-key"] }),
            new ReasoningDelta("the key is secret-key"),
            new ToolCallRequested("c1", "RunShell", Arguments(("command", "echo secret-key"))));

        string output = Normalize(console.Output);
        Assert.DoesNotContain("secret-key", output);
        Assert.Contains("the key is [redacted]", output);
        Assert.Contains("echo [redacted]", output);
    }

    [Fact]
    public void NestedRun_IsDrawnAsLabelledLinesUnderTheToolCall_WithoutItsAnswerOrResultBodies()
    {
        TestConsole console = new();
        Guid outer = Guid.NewGuid();
        Guid inner = Guid.NewGuid();
        T Nested<T>(T item) where T : AgentEvent => item with { RunId = inner, ParentRunId = outer, AgentName = "Finder" };

        Draw(new AgentConsole(console),
            new ToolCallRequested("c1", "RunSubAgent", Arguments(("modelName", "luna"))) { RunId = outer },
            Nested(new RunStarted()),
            Nested(new ReasoningDelta("streamed thought")),
            Nested(new ReasoningCompleted("The docs\nlist 0.57.2.")),
            Nested(new ToolCallRequested("n1", "Search", Arguments(("query", "tea")))),
            Nested(new ToolCallCompleted("n1", "Search", ToolCallStatus.Succeeded, "a long page body", null, TimeSpan.FromSeconds(1.4))),
            Nested(new AssistantTextDelta("the nested answer")),
            Nested(new AssistantTextCompleted("the nested answer")),
            Nested(new RunCompleted(RunOutcome.Succeeded, null, TimeSpan.FromSeconds(6.2), new UsageDetails { TotalTokenCount = 4310 })),
            new ToolCallCompleted("c1", "RunSubAgent", ToolCallStatus.Succeeded, "the nested answer", null, TimeSpan.FromSeconds(6.3)) { RunId = outer });

        Assert.Equal(Normalize(
            """
              RunSubAgent  modelName: luna
                ▸ Finder started
                Finder: thinking: The docs list 0.57.2.
                Finder: Search  query: tea
                ✓ Finder: Search  done in 1.4 s
                ◂ Finder done in 6.2 s, 4,310 tokens
              ✓ RunSubAgent  done in 6.3 s
                the nested answer

            """), Normalize(console.Output));
    }

    [Fact]
    public void NestedRunsThatOverlap_StayOnSeparateLabelledLines_AndARunTwoLevelsDownIsIndentedTwice()
    {
        TestConsole console = new();
        Guid outer = Guid.NewGuid();
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        Guid deep = Guid.NewGuid();

        Draw(new AgentConsole(console),
            new RunStarted { RunId = first, ParentRunId = outer, AgentName = "First" },
            new RunStarted { RunId = second, ParentRunId = outer, AgentName = "Second" },
            new ToolCallRequested("a", "Read", Arguments()) { RunId = first, ParentRunId = outer, AgentName = "First" },
            new ToolCallRequested("b", "Find", Arguments()) { RunId = second, ParentRunId = outer, AgentName = "Second" },
            new RunStarted { RunId = deep, ParentRunId = second, AgentName = "Deep" },
            new RunCompleted(RunOutcome.Failed, new InvalidOperationException("boom"), TimeSpan.FromSeconds(2)) { RunId = deep, ParentRunId = second, AgentName = "Deep" },
            new RunCompleted(RunOutcome.Cancelled, null, TimeSpan.FromSeconds(3)) { RunId = second, ParentRunId = outer, AgentName = "Second" });

        string[] lines = Normalize(console.Output).TrimEnd('\n').Split('\n');
        Assert.Equal(
            [
                "    ▸ First started",
                "    ▸ Second started",
                "    First: Read  ",
                "    Second: Find  ",
                "      ▸ Deep started",
                "      ✗ Deep failed (InvalidOperationException) after 2.0 s",
                "    ✗ Second stopped after 3.0 s"
            ], lines);
    }

    [Fact]
    public void NestedRun_IsRedacted_NamesAnUnnamedAgent_AndSaysWhenAToolWasNotAllowed()
    {
        TestConsole console = new();
        Guid outer = Guid.NewGuid();
        Guid inner = Guid.NewGuid();

        Draw(new AgentConsole(console, new AgentConsoleOptions { SensitiveValues = ["secret-key"] }),
            new ReasoningCompleted("the key is secret-key") { RunId = inner, ParentRunId = outer },
            new ToolCallRequested("n1", "RunShell", Arguments(("command", "echo secret-key"))) { RunId = inner, ParentRunId = outer },
            new ToolCallCompleted("n1", "RunShell", ToolCallStatus.Rejected, null, null, TimeSpan.Zero) { RunId = inner, ParentRunId = outer });

        string output = Normalize(console.Output);
        Assert.DoesNotContain("secret-key", output);
        Assert.Contains("    sub-agent: thinking: the key is [redacted]", output);
        Assert.Contains("echo [redacted]", output);
        Assert.Contains("    ⊘ sub-agent: RunShell  not allowed", output);
    }

    [Fact]
    public void NestedRun_EndsTheOuterAgentsOpenLineFirst()
    {
        TestConsole console = new();

        Draw(new AgentConsole(console),
            new AssistantTextDelta("Let me look"),
            new RunStarted { RunId = Guid.NewGuid(), ParentRunId = Guid.NewGuid(), AgentName = "Finder" });

        Assert.Equal("  Let me look\n    ▸ Finder started\n", Normalize(console.Output));
    }

    [Fact]
    public void NestedRuns_AreNotDrawn_WhenTheOptionIsOff()
    {
        TestConsole console = new();

        Draw(new AgentConsole(console, new AgentConsoleOptions { ShowNestedRuns = false }),
            new RunStarted { ParentRunId = Guid.NewGuid(), AgentName = "Finder" },
            new ToolCallRequested("n1", "Search", Arguments()) { ParentRunId = Guid.NewGuid(), AgentName = "Finder" });

        Assert.Equal("", console.Output);
    }

    [Fact]
    public void Steps_AreNamed_AndChecksShowTheOutcomeAndEachProblem()
    {
        TestConsole console = new();

        Draw(new AgentConsole(console),
            new StepStarted("draft", IsFinal: false),
            new StepChecked("review", Passed: false, "Too long.\nNo date."),
            new StepChecked("notes", Passed: true, null));

        string output = Normalize(console.Output);
        Assert.Contains("Step: draft", output);
        Assert.Contains("Check: review found 2 problems", output);
        Assert.Contains("    - Too long.", output);
        Assert.Contains("    - No date.", output);
        Assert.Contains("Check: notes passed", output);
    }

    [Fact]
    public void WriteLine_EndsAStreamedLineFirst()
    {
        TestConsole console = new();
        AgentConsole sample = new(console);

        sample.Write("partial");
        sample.WriteLine("whole line");

        Assert.Contains("  partial\nwhole line\n", Normalize(console.Output));
    }

    [Fact]
    public async Task ConfirmToolCallAsync_AllowReturnsTrueAndShowsTheArguments()
    {
        TestConsole console = new TestConsole().Interactive();
        console.Input.PushKey(ConsoleKey.Enter);

        bool approved = await new AgentConsole(console).ConfirmToolCallAsync(Call("c1", "CloseTicket", ("number", "PR-8")), TestContext.Current.CancellationToken);

        Assert.True(approved);
        Assert.Contains("Allow this tool call?", console.Output);
        Assert.Contains("CloseTicket", console.Output);
        Assert.Contains("PR-8", console.Output);
        Assert.Contains("Approved.", console.Output);
    }

    [Fact]
    public async Task ToolArguments_AreDrawnAsTheTextTheyAre_WithoutJsonEscapes_OnTheLineAndInThePanel()
    {
        const string Command = "python3 -c \"\nimport re\nprint('<b>' + \\\"a & b\\\")\n\" && echo done";
        TestConsole console = new TestConsole().Interactive();
        console.Profile.Width = 300;
        console.Input.PushKey(ConsoleKey.Enter);
        AgentConsole sample = new(console, new AgentConsoleOptions { SensitiveValues = ["s3cret"] });
        FunctionCallContent call = Call("c1", "RunShell", ("command", Command), ("timeoutSeconds", 30), ("outputFilters", new[] { "error", "s3cret" }));

        sample.WriteEvent(new ToolCallRequested("c1", "RunShell", call.Arguments!.ToDictionary()));
        await sample.ConfirmToolCallAsync(call, TestContext.Current.CancellationToken);

        string output = Plain(console.Output);
        Assert.DoesNotContain("\\u00", output);
        Assert.DoesNotContain("\\n", output);
        // The line: one row, line breaks as spaces.
        Assert.Contains("  RunShell  command: python3 -c \" import re print('<b>' + \\\"a & b\\\") \" && echo done, timeoutSeconds: 30, outputFilters: [\"error\",\"[redacted]\"]", output);
        // The panel: one argument per row, and the script's own lines under its name.
        string[] rows = [.. output.Split('\n').Select(row => row.Trim().Trim('│').TrimEnd())];
        Assert.Contains(" RunShell", rows);
        Assert.Contains(" command:", rows);
        Assert.Contains("   python3 -c \"", rows);
        Assert.Contains("   import re", rows);
        Assert.Contains("   print('<b>' + \\\"a & b\\\")", rows);
        Assert.Contains("   \" && echo done", rows);
        Assert.Contains(" timeoutSeconds: 30", rows);
        Assert.Contains(" outputFilters: [\"error\",\"[redacted]\"]", rows);
    }

    [Fact]
    public async Task ApprovalPanel_CutsALongArgumentAtTheLimit_AndCountsTheRest()
    {
        TestConsole console = new TestConsole().Interactive();
        console.Input.PushKey(ConsoleKey.Enter);
        AgentConsole sample = new(console, new AgentConsoleOptions { MaxApprovalLines = 3 });
        string script = string.Join("\n", Enumerable.Range(1, 10).Select(number => $"step {number}"));

        await sample.ConfirmToolCallAsync(Call("c1", "RunShell", ("command", script)), TestContext.Current.CancellationToken);

        string output = Plain(console.Output);
        Assert.Contains("step 3", output);
        Assert.DoesNotContain("step 4", output);
        Assert.Contains("… 7 more lines", output);
        Assert.Contains("Allow this tool call?", output);
    }

    [Fact]
    public async Task ConfirmToolCallAsync_SkipReturnsFalse()
    {
        TestConsole console = new TestConsole().Interactive();
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Enter);

        bool approved = await new AgentConsole(console).ConfirmToolCallAsync(Call("c1", "CloseTicket", ("number", "PR-8")), TestContext.Current.CancellationToken);

        Assert.False(approved);
        Assert.Contains("Skipped.", console.Output);
    }

    [Fact]
    public async Task ConfirmToolCallAsync_WithASessionRule_OffersItBetweenAllowAndSkip_AndReturnsThatChoice()
    {
        TestConsole console = new TestConsole().Interactive();
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Enter);
        ApprovalQuestion question = new(Call("c1", "RunShell", ("command", "dotnet build -c Release")), AllowRule.ForPrefix("RunShell", "command", "dotnet build"));

        ApprovalChoice choice = await new AgentConsole(console).ConfirmToolCallAsync(question, TestContext.Current.CancellationToken);

        Assert.Equal(ApprovalChoice.AllowForSession, choice);
        string output = Plain(console.Output);
        Assert.Contains("Allow for this session: RunShell starting with \"dotnet build\"", output);
        Assert.Contains("Approved for this session: RunShell starting with \"dotnet build\".", output);
    }

    [Fact]
    public async Task ConfirmToolCallAsync_WithASessionRule_StillSkipsOnTheLastChoice_AndWithoutOne_OffersTwoChoices()
    {
        TestConsole console = new TestConsole().Interactive();
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);
        AgentConsole sample = new(console);

        ApprovalChoice skipped = await sample.ConfirmToolCallAsync(new ApprovalQuestion(Call("c1", "EditFile", ("path", "a.cs")), AllowRule.ForTool("EditFile")), TestContext.Current.CancellationToken);
        Assert.Equal(ApprovalChoice.Skip, skipped);
        Assert.Contains("Allow for this session: every EditFile call", Plain(console.Output));

        TestConsole plain = new TestConsole().Interactive();
        plain.Input.PushKey(ConsoleKey.Enter);
        ApprovalChoice allowed = await new AgentConsole(plain).ConfirmToolCallAsync(new ApprovalQuestion(Call("c1", "EditFile", ("path", "a.cs"))), TestContext.Current.CancellationToken);
        Assert.Equal(ApprovalChoice.Allow, allowed);
        Assert.DoesNotContain("for this session", Plain(plain.Output));
    }

    [Fact]
    public async Task ConfirmToolCallAsync_WithoutAnInteractiveTerminal_SkipsAQuestionThatOffersASessionRule()
    {
        TestConsole console = new();

        ApprovalChoice choice = await new AgentConsole(console).ConfirmToolCallAsync(new ApprovalQuestion(Call("c1", "EditFile"), AllowRule.ForTool("EditFile")), TestContext.Current.CancellationToken);

        Assert.Equal(ApprovalChoice.Skip, choice);
    }

    [Fact]
    public async Task ConfirmToolCallAsync_WithoutAnInteractiveTerminalSkipsAndSaysWhy()
    {
        TestConsole console = new();

        bool approved = await new AgentConsole(console).ConfirmToolCallAsync(Call("c1", "CloseTicket", ("number", "PR-8")), TestContext.Current.CancellationToken);

        Assert.False(approved);
        Assert.Contains("no interactive terminal", console.Output);
    }

    [Fact]
    public async Task ReadLineAsync_WhenInteractive_PromptsAndReturnsInput()
    {
        TestConsole console = new();
        console.Profile.Capabilities.Interactive = true;
        console.Input.PushTextWithEnter("build the project");

        string? input = await new AgentConsole(console).ReadLineAsync("You › ", TestContext.Current.CancellationToken);

        Assert.Equal("build the project", input);
        Assert.Contains("You ›", console.Output);
    }

    [Fact]
    public async Task ReadLineAsync_WithoutAnInteractiveTerminal_ReturnsNull()
    {
        TestConsole console = new();

        string? input = await new AgentConsole(console).ReadLineAsync("You › ", TestContext.Current.CancellationToken);

        Assert.Null(input);
    }

    [Fact]
    public void Options_OverrideTheColoursAndTheIndent()
    {
        TestConsole console = new TestConsole().EmitAnsiSequences();
        AgentConsole sample = new(console, new AgentConsoleOptions { Muted = "red", Indent = ">> " });

        Draw(sample, new StepStarted("Plan", false));

        string output = Normalize(console.Output);
        Assert.Contains(">> \u001b[38;5;9mStep: Plan", output);
    }

    [Fact]
    public void Options_BoundTheArgumentLineAndTheResultLines()
    {
        TestConsole console = new();
        AgentConsole sample = new(console, new AgentConsoleOptions { MaxLineLength = 20, MaxResultLines = 2 });

        Draw(sample,
            new ToolCallRequested("c1", "Search", Arguments(("query", new string('q', 100)))),
            Completed("c1", "Search", "one\ntwo\nthree\nfour"));

        string output = Normalize(console.Output);
        Assert.Contains("…", output);
        Assert.DoesNotContain(new string('q', 30), output);
        Assert.Contains("    two\n", output);
        Assert.DoesNotContain("three", output);
        Assert.Contains("… 2 more lines", output);
    }

    [Fact]
    public void Options_SensitiveValuesAreRedactedInWriteAnswer()
    {
        TestConsole console = new();

        new AgentConsole(console, new AgentConsoleOptions { SensitiveValues = ["hunter2"] }).WriteAnswer("the password is hunter2");

        string output = Normalize(console.Output);
        Assert.DoesNotContain("hunter2", output);
        Assert.Contains("the password is [redacted]", output);
    }

    [Fact]
    public void BackgroundTask_IsDrawnAsOneLineWhenItStartsAndOneWhenItEnds_WithItsIdAndTool()
    {
        TestConsole console = new();
        Guid outer = Guid.NewGuid();
        Guid inner = Guid.NewGuid();
        T Nested<T>(T item) where T : AgentEvent => item with { RunId = inner, ParentRunId = outer, AgentName = "Finder" };
        BackgroundTaskEnded Ended(int id, string tool, BackgroundTaskState status, bool didNotStop, double seconds) =>
            new(id, tool, status, didNotStop, null, null, TimeSpan.FromSeconds(seconds)) { RunId = outer };

        Draw(new AgentConsole(console),
            new BackgroundTaskStarted(1, "RunSubAgent", null) { RunId = outer },
            Ended(1, "RunSubAgent", BackgroundTaskState.Completed, false, 4.2),
            Ended(2, "RunShell", BackgroundTaskState.Failed, false, 0.3),
            Ended(3, "RunShell", BackgroundTaskState.TimedOut, true, 1800),
            Ended(4, "RunShell", BackgroundTaskState.Cancelled, false, 2),
            Nested(new RunStarted()),
            Nested(new BackgroundTaskStarted(1, "Search", null)),
            Nested(new BackgroundTaskEnded(1, "Search", BackgroundTaskState.Completed, false, null, null, TimeSpan.FromSeconds(1.5))));

        Assert.Equal(Normalize(
            """
              ▸ task 1 (RunSubAgent) started in the background
              ✓ task 1 (RunSubAgent)  done in 4.2 s
              ✗ task 2 (RunShell)  failed after 0.3 s
              ✗ task 3 (RunShell)  timed out after 1800.0 s, its tool did not stop
              ✗ task 4 (RunShell)  cancelled after 2.0 s
                ▸ Finder started
                ▸ Finder: task 1 (Search) started in the background
                ✓ Finder: task 1 (Search)  done in 1.5 s

            """), Normalize(console.Output));
    }

    [Fact]
    public void BackgroundStart_IsDrawnOnce_AndTheRunSaysWhenItWaitsAndWhenItStopsWaiting()
    {
        TestConsole console = new();
        Guid run = Guid.NewGuid();

        Draw(new AgentConsole(console),
            new ToolCallRequested("c1", "RunShell", Arguments(("command", "make"))) { RunId = run },
            new BackgroundTaskStarted(1, "RunShell", "c1") { RunId = run },
            // The call's own result is the start message; drawing it would say the same thing twice.
            new ToolCallCompleted("c1", "RunShell", ToolCallStatus.Succeeded, "Started task 1 (RunShell).", null, TimeSpan.Zero) { RunId = run },
            new BackgroundWaitStarted([1]) { RunId = run },
            new BackgroundWaitStarted([1, 2]) { RunId = run },
            new BackgroundWakeLimitReached(10, [1, 2]) { RunId = run });

        Assert.Equal(Normalize(
            """
              RunShell  command: make
              ▸ task 1 (RunShell) started in the background
              … waiting for 1 background task
              … waiting for 2 background tasks
              ✗ stopped waiting at the limit of 10 wakes, 2 background tasks cancelled

            """), Normalize(console.Output));
    }

    private static void Draw(AgentConsole console, params AgentEvent[] events)
    {
        foreach (AgentEvent item in events)
        {
            console.WriteEvent(item);
        }
    }

    private static ToolCallCompleted Completed(string callId, string name, string result) =>
        new(callId, name, ToolCallStatus.Succeeded, result, null, TimeSpan.FromSeconds(0.8));

    private static IReadOnlyDictionary<string, object?> Arguments(params (string Name, object? Value)[] arguments) =>
        arguments.ToDictionary(argument => argument.Name, argument => argument.Value);

    private static FunctionCallContent Call(string id, string name, params (string Name, object? Value)[] arguments) =>
        new(id, name, arguments.ToDictionary(argument => argument.Name, argument => argument.Value));

    private static string Plain(string output) => System.Text.RegularExpressions.Regex.Replace(Normalize(output), "\u001b\\[[0-9;?]*[A-Za-z]", "");

    // Also for an expected raw string: it has the line endings the file was checked out with, CRLF on Windows.
    private static string Normalize(string text) => text.ReplaceLineEndings("\n");
}
