using Microsoft.Extensions.AI;
using PinkRooster.Agents.Tests.TestSupport;
using PinkRooster.Samples.Tests.TestSupport;
using static PinkRooster.Samples.Tests.TestSupport.Replies;
using Sample = PinkRooster.Samples.Middleware.Program;

namespace PinkRooster.Samples.Tests.Middleware;

public sealed class MiddlewareSampleTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AReplyThatPromisesARefund_PublishesTheCustomEvent()
    {
        ScriptedChatClient model = new(Text("We will send you a refund today."));
        RecordingConsole console = new();

        await Sample.RunAsync(model, console, Token);

        Assert.Contains("Escalated to a person: The reply promises a refund.", console.Output);
        Assert.Contains("We will send you a refund today.", console.Output);
    }

    [Fact]
    public async Task AReplyWithoutARefund_PublishesNothing()
    {
        ScriptedChatClient model = new(Text("It is on its way."));
        RecordingConsole console = new();

        await Sample.RunAsync(model, console, Token);

        Assert.DoesNotContain("Escalated", console.Output);
    }

    [Fact]
    public async Task WithoutAModelServer_ExitsWithOne_AndSaysWhereToLook() =>
        await SampleChecks.AssertExitsNamingModelsFile(Sample.RunAsync);
}
