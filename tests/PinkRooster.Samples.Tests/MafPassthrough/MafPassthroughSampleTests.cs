using Microsoft.Extensions.AI;
using PinkRooster.Agents.Tests.TestSupport;
using PinkRooster.Samples.Tests.TestSupport;
using static PinkRooster.Samples.Tests.TestSupport.Replies;
using Sample = PinkRooster.Samples.MafPassthrough.Program;

namespace PinkRooster.Samples.Tests.MafPassthrough;

public sealed class MafPassthroughSampleTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheOptionsReachMaf_AndTheHistoryCarriesTheSecondQuestion()
    {
        ScriptedChatClient model = new(Call("c1", "GetTicket", new() { ["number"] = "PR-7" }), Text("PR-7 is open."), Text("Yes, it is open."));
        RecordingConsole console = new();

        await Sample.RunAsync(model, console, Token);

        Assert.Equal(3, model.Requests.Count);
        Assert.All(model.Options, options => Assert.Equal(0.2f, options!.Temperature));
        Assert.All(model.Options, options => Assert.Contains("The shop is open from 9 to 17 on weekdays.", options!.Instructions));
        // The third call is the second question: the first answer is still in its history.
        Assert.Contains(model.Requests[2], message => message.Text == "PR-7 is open.");
        Assert.Equal(3, console.Output.Split("model call with").Length - 1);
        Assert.Contains("Triages support tickets.", console.Output);
    }

    [Fact]
    public async Task WithoutAModelServer_ExitsWithOne_AndSaysWhereToLook() =>
        await SampleChecks.AssertExitsNamingModelsFile(Sample.RunAsync);
}
