using Microsoft.Extensions.AI;
using PinkRooster.Agents.Tests.TestSupport;
using PinkRooster.Samples.Tests.TestSupport;
using static PinkRooster.Samples.Tests.TestSupport.Replies;
using Sample = PinkRooster.Samples.Approvals.Program;

namespace PinkRooster.Samples.Tests.Approvals;

public sealed class ApprovalsSampleTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AskedOncePerApproval_AndContinuesOnTheSameSession()
    {
        ScriptedChatClient model = new(
            Call("c1", "CloseTicket", new() { ["number"] = "PR-8" }),
            Call("c2", "ArchiveTicket", new() { ["number"] = "PR-8" }),
            Text("PR-8 is closed and archived."));
        RecordingConsole console = new();

        await Sample.RunAsync(model, console, Token);

        Assert.Equal(["CloseTicket", "ArchiveTicket"], console.ApprovalsAsked);
        Assert.Contains("PR-8 is closed and archived.", console.Output);
        Assert.Contains(model.Requests[1], message => message.Contents.OfType<FunctionResultContent>().Any(result => result.Result?.ToString() == "PR-8 closed."));
    }

    [Fact]
    public async Task ASkippedCall_DoesNotRun_AndTheModelIsToldWhy()
    {
        ScriptedChatClient model = new(Call("c1", "CloseTicket", new() { ["number"] = "PR-8" }), Text("I did not close it."));
        RecordingConsole console = new();
        console.Approvals.Enqueue(false);

        await Sample.RunAsync(model, console, Token);

        Assert.Equal(["CloseTicket"], console.ApprovalsAsked);
        string seen = string.Join("\n", model.Requests[1].SelectMany(message => message.Contents).Select(content => content.ToString()));
        Assert.DoesNotContain("PR-8 closed.", seen);
        Assert.Contains(model.Requests[1].SelectMany(message => message.Contents).OfType<FunctionResultContent>(), result => result.Result?.ToString()?.Contains("The user did not allow this.") == true);
        Assert.Contains("I did not close it.", console.Output);
    }

    [Fact]
    public async Task WithoutAModelServer_ExitsWithOne_AndSaysWhereToLook() =>
        await SampleChecks.AssertExitsNamingModelsFile(Sample.RunAsync, needsTerminal: true);

    [Fact]
    public async Task WithoutATerminal_ExitsWithOne_AndSaysSo() =>
        await SampleChecks.AssertNeedsATerminal(Sample.RunAsync);
}
