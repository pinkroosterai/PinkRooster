using Microsoft.Extensions.AI;
using PinkRooster.Agents.Tests.TestSupport;
using PinkRooster.Samples.Tests.TestSupport;
using static PinkRooster.Samples.Tests.TestSupport.Replies;
using Sample = PinkRooster.Samples.OtherAgents.Program;

namespace PinkRooster.Samples.Tests.OtherAgents;

public sealed class OtherAgentsSampleTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task BothWaysGiveTheModelTheToolsAndTheContext()
    {
        ScriptedChatClient model = new(
            Call("c1", "GetTicket", new() { ["number"] = "PR-7" }), Text("PR-7 is open."),
            Call("c2", "GetTicket", new() { ["number"] = "PR-8" }), Text("PR-8 is open."));
        RecordingConsole console = new();

        await Sample.RunAsync(model, console, Token);

        Assert.Equal(4, model.Requests.Count);
        Assert.All(model.Options, options => Assert.Contains(options!.Tools!, tool => tool.Name == "GetTicket"));
        // The provider sends the context once per run, in the instructions.
        Assert.Contains("## Open tickets", model.Options[0]!.Instructions);
        // Through the client the context comes before every call, the tool loop's second call included.
        Assert.Contains(model.Requests[3], message => message.Text.Contains("## Open tickets"));
        Assert.Contains("PR-8 is open.", console.Output);
    }

    [Fact]
    public async Task WithoutAModelServer_ExitsWithOne_AndSaysWhereToLook() =>
        await SampleChecks.AssertExitsNamingModelsFile(Sample.RunAsync);
}
