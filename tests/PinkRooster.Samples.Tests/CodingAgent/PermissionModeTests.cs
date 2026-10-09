using PinkRooster.Agents.Permissions;
using PinkRooster.Agents.Tests.TestSupport;
using static PinkRooster.Samples.Tests.CodingAgent.Showcase;

namespace PinkRooster.Samples.Tests.CodingAgent;

/// <summary>The three permission modes on the showcase: what asks, what runs, what is refused.</summary>
public sealed class PermissionModeTests
{
    [Fact]
    public async Task InAsk_AScriptedEdit_AsksOnce_AndTheFileChanges()
    {
        using Showcase showcase = new();
        showcase.Write("a.txt", "one\n");
        ScriptedChatClient model = new(Edit("a.txt", "one", "two"), Text("edited"), Text("checked"));

        int exit = await showcase.RunAsync(model, "change it");

        Assert.Equal(0, exit);
        Assert.Equal(["EditFile"], showcase.Console.ApprovalsAsked.Select(question => question.Call.Name));
        Assert.Equal("two\n", File.ReadAllText(showcase.PathOf("a.txt")));
    }

    [Fact]
    public async Task InAcceptEdits_AScriptedEdit_AsksNothing_AndTheFileChanges_AndACommandStillAsks()
    {
        using Showcase showcase = new();
        showcase.Write("a.txt", "one\n");
        ScriptedChatClient model = new(Edit("a.txt", "one", "two"), Text("edited"), Shell("echo checked"), Text("checked"));

        await showcase.RunAsync(model, "/mode accept-edits", "change it");

        Assert.Equal(["RunShell"], showcase.Console.ApprovalsAsked.Select(question => question.Call.Name));
        Assert.Equal("two\n", File.ReadAllText(showcase.PathOf("a.txt")));
        Assert.Contains("The permission mode is accept-edits.", showcase.Console.Output);
    }

    [Fact]
    public async Task InPlan_AScriptedEdit_AsksNothing_TheFileIsUnchanged_AndTheModelIsToldWhy()
    {
        using Showcase showcase = new();
        showcase.Write("a.txt", "one\n");
        ScriptedChatClient model = new(Edit("a.txt", "one", "two"), Text("here is the plan"));

        await showcase.RunAsync(model, "/mode plan", "change it");

        Assert.Empty(showcase.Console.ApprovalsAsked);
        Assert.Equal("one\n", File.ReadAllText(showcase.PathOf("a.txt")));
        Assert.Contains("permission mode is 'plan'", Assert.Single(Results(model)).Result);
        // A refused change is no change: the run ends without a verify step.
        Assert.Equal(["start"], showcase.Steps);
        // The model reads the mode before every model call.
        Assert.Contains(model.Requests[0], message => message.Text.Contains("Permission mode: plan."));
    }

    [Fact]
    public async Task ASessionWideAnswer_CoversTheNextCall_UntilClear()
    {
        using Showcase showcase = new();
        ScriptedChatClient model = new(
            Shell("echo -n one"), Text("ran"),
            Shell("echo -n two"), Text("ran again"),
            Shell("echo -n three"), Text("ran after the clear"));
        showcase.Console.Approvals.Enqueue(ApprovalChoice.AllowForSession);

        await showcase.RunAsync(model, "run it", "run it again", "/clear", "and once more");

        // The first call asked and offered the command's start; the second ran on that answer; after /clear the same command asks again.
        Assert.Equal(["echo -n one", "echo -n three"], showcase.Console.ApprovalsAsked.Select(question => question.Call.Arguments!["command"]!.ToString()));
        Assert.Equal("RunShell starting with \"echo\"", showcase.Console.ApprovalsAsked[0].SessionRule?.ToString());
    }

    [Fact]
    public async Task AnAllowRuleOfTheConfigFile_RunsUnasked_AlsoInPlan()
    {
        using Showcase showcase = new();
        showcase.Config("""{ "allow": [ { "tool": "RunShell", "argument": "command", "prefix": "echo" } ] }""");
        ScriptedChatClient model = new(Shell("echo allowed"), Shell("printf refused"), Text("planned"));

        await showcase.RunAsync(model, "/mode plan", "look around");

        Assert.Empty(showcase.Console.ApprovalsAsked);
        IReadOnlyList<(string Name, string Result)> results = Results(model);
        Assert.Contains("allowed", results[0].Result);
        Assert.Contains("permission mode is 'plan'", results[1].Result);
    }
}
