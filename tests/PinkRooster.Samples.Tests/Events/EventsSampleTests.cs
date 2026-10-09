using Microsoft.Extensions.AI;
using PinkRooster.Agents.Tests.TestSupport;
using PinkRooster.Samples.Tests.TestSupport;
using static PinkRooster.Samples.Tests.TestSupport.Replies;
using Sample = PinkRooster.Samples.Events.Program;

namespace PinkRooster.Samples.Tests.Events;

public sealed class EventsSampleTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ARunAndAStreamedRun_PublishTheSameKindsOfEvent()
    {
        ScriptedChatClient model = new(Call("c1", "GetTicket", new() { ["number"] = "PR-7" }), Text("PR-7 is open."), Text("PR-8 is open."));
        RecordingConsole console = new();

        await Sample.RunAsync(model, console, Token);

        string output = console.Output;
        string[] parts = output.Split("RunStreamingAsync:");
        Assert.Contains("run started", parts[0]);
        Assert.Contains("-> GetTicket", parts[0]);
        Assert.Contains("<- GetTicket: Succeeded", parts[0]);
        Assert.Contains("answer: PR-7 is open.", parts[0]);
        Assert.Contains("run Succeeded", parts[0]);
        Assert.Contains("run started", parts[1]);
        Assert.Contains("answer: PR-8 is open.", parts[1]);
        Assert.Contains("run Succeeded", parts[1]);
        Assert.Contains("PR-8 is open.", parts[1]);
    }

    [Fact]
    public async Task WithoutAModelServer_ExitsWithOne_AndSaysWhereToLook() =>
        await SampleChecks.AssertExitsNamingModelsFile(Sample.RunAsync);
}
