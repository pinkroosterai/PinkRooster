using Microsoft.Extensions.AI;
using PinkRooster.Agents.Tests.TestSupport;
using PinkRooster.Samples.Tests.TestSupport;
using static PinkRooster.Samples.Tests.TestSupport.Replies;
using Sample = PinkRooster.Samples.SteppedAgents.Program;

namespace PinkRooster.Samples.Tests.SteppedAgents;

public sealed class SteppedAgentsSampleTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TranslatesThenRevises_AndAnswersWithTheLastReply()
    {
        ScriptedChatClient model = new(Text("Hello, wolrd"), Text("Hello, world"));
        RecordingConsole console = new();

        await Sample.RunAsync(model, console, Token);

        Assert.Equal(2, model.Requests.Count);
        Assert.Contains("step: start", console.Output);
        Assert.Contains("step: revise", console.Output);
        Assert.Contains("Hello, world", console.Output.Split("step: revise")[1]);
        Assert.Contains(model.Requests[1], message => message.Text.Contains("Hallo wereld, dit is een test van de vertaler."));
    }

    [Fact]
    public async Task WithoutAModelServer_ExitsWithOne_AndSaysWhereToLook() =>
        await SampleChecks.AssertExitsNamingModelsFile(Sample.RunAsync);
}
