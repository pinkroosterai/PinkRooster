using PinkRooster.Agents.Tests.TestSupport;
using static PinkRooster.Samples.Tests.CodingAgent.Showcase;
using Sample = PinkRooster.Samples.CodingAgent;

namespace PinkRooster.Samples.Tests.CodingAgent;

/// <summary>The assistant's own step: after a turn that changed files it checks its work, within the turn limit.</summary>
public sealed class VerifyStepTests
{
    [Fact]
    public async Task ATurnWithAnEdit_IsFollowedByAVerifyStep_AndATurnWithoutOneIsNot()
    {
        using Showcase showcase = new();
        showcase.Write("a.txt", "one\n");
        ScriptedChatClient model = new(Edit("a.txt", "one", "two"), Text("edited"), Text("nothing to build here"), Text("just an answer"));

        await showcase.RunAsync(model, "change it", "and explain it");

        // The first message: the turn with the edit, then the verify turn. The second message: one turn.
        Assert.Equal(["start", "verify", "start"], showcase.Steps);
        Assert.Equal(4, model.Requests.Count);
        Assert.Contains(model.Requests[2], message => message.Text.StartsWith("You changed files. Find how this project is built and tested"));
    }

    [Fact]
    public async Task AnEditThatFails_IsNoChange_SoNoVerifyStepFollows()
    {
        using Showcase showcase = new();
        showcase.Write("a.txt", "one\n");
        ScriptedChatClient model = new(Edit("a.txt", "not in the file", "two"), Text("could not edit"));

        await showcase.RunAsync(model, "change it");

        Assert.Equal(["start"], showcase.Steps);
        Assert.StartsWith("Error:", Assert.Single(Results(model)).Result);
    }

    [Fact]
    public async Task WithAnEditInEveryTurn_AndAVerifyCommandThatAlwaysFails_TheRunEndsAfterThreeTurns()
    {
        using Showcase showcase = new();
        showcase.Write("a.txt", "0\n");
        showcase.Config("""{ "verifyCommand": "exit 3" }""");
        ScriptedChatClient model = new(
            Edit("a.txt", "0", "1"), Text("first try"),
            Shell("exit 3"), Edit("a.txt", "1", "2"), Text("second try"),
            Shell("exit 3"), Edit("a.txt", "2", "3"), Text("still failing: exit code 3"),
            Text("never asked"));

        await showcase.RunAsync(model, "fix it");

        // Three turns: the first and two verify turns. Nothing asks for a fourth, though the last turn changed a file too.
        Assert.Equal(2, VerifyTurns(model));
        Assert.Equal(8, model.Requests.Count);
        Assert.Equal("3\n", File.ReadAllText(showcase.PathOf("a.txt")));
        Assert.Contains(model.Requests[2], message => message.Text.StartsWith("You changed files. Run `exit 3` with RunShell."));
        // The project's own verify command runs unasked in ask mode; the edits ask.
        Assert.Equal(["EditFile", "EditFile", "EditFile"], showcase.Console.ApprovalsAsked.Select(question => question.Call.Name));
        Assert.Contains(Results(model), result => result.Name == "RunShell" && result.Result.Contains("3"));
    }

    [Fact]
    public async Task WithNoEditInTheVerifyTurn_TheRunEndsAfterTwoTurns_WhetherTheCommandPassedOrNot()
    {
        using Showcase showcase = new();
        showcase.Write("a.txt", "0\n");
        showcase.Config("""{ "verifyCommand": "exit 3" }""");
        ScriptedChatClient model = new(Edit("a.txt", "0", "1"), Text("first try"), Shell("exit 3"), Text("it fails with exit code 3 and I cannot fix it"), Text("never asked"));

        await showcase.RunAsync(model, "fix it");

        Assert.Equal(["start", "verify"], showcase.Steps);
        Assert.Equal(1, VerifyTurns(model));
        Assert.Equal(4, model.Requests.Count);
    }

    [Fact]
    public async Task TheConfigFilesTurnLimit_Counts()
    {
        using Showcase showcase = new();
        showcase.Write("a.txt", "0\n");
        showcase.Config("""{ "maxTurns": 1 }""");
        ScriptedChatClient model = new(Edit("a.txt", "0", "1"), Text("edited"), Text("never asked"));

        await showcase.RunAsync(model, "fix it");

        Assert.Equal(["start"], showcase.Steps);
        Assert.Equal(2, model.Requests.Count);
    }

    [Fact]
    public async Task TheRunThatInitStarts_WritesTheInstructionFile_AndHasNoVerifyStep()
    {
        using Showcase showcase = new();
        ScriptedChatClient model = new(Call("CreateFile", ("path", "AGENTS.md"), ("content", "# Notes\nBuild with make.\n")), Text("written"), Text("never asked"));

        await showcase.RunAsync(model, "/init");

        Assert.Equal("# Notes\nBuild with make.\n", File.ReadAllText(showcase.PathOf("AGENTS.md")));
        Assert.Equal(["start"], showcase.Steps);
        Assert.Equal(2, model.Requests.Count);
        Assert.Equal(1, model.Requests[0].Count(message => message.Text == Sample.CodingAssistant.InitPrompt));
    }
}
