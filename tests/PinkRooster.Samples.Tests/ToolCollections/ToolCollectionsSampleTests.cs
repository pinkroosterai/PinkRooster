using Microsoft.Extensions.AI;
using PinkRooster.Agents.Tests.TestSupport;
using PinkRooster.Samples.Tests.TestSupport;
using static PinkRooster.Samples.Tests.TestSupport.Replies;
using Sample = PinkRooster.Samples.ToolCollections.Program;

namespace PinkRooster.Samples.Tests.ToolCollections;

public sealed class ToolCollectionsSampleTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheCollectionsTextReachesThePrompt_AndItsContextEveryCall()
    {
        ScriptedChatClient model = new(Call("c1", "GetOrder", new() { ["number"] = "A-1001" }), Text("Order A-1001 has shipped."));
        RecordingConsole console = new();

        await Sample.RunAsync(model, console, Token);

        string prompt = model.Options[0]!.Instructions!;
        Assert.Contains("Order numbers look like A-1234.", prompt);
        Assert.Contains("Totals are in euro.", prompt);
        Assert.Contains("Never tell a customer about an order that is not theirs.", prompt);
        Assert.Contains("Order numbers look like A-1234.", console.Output);
        // The note on the current state comes before each of the two model calls.
        Assert.All(model.Requests, request => Assert.Contains(request, message => message.Text.Contains("## Open orders\n2")));
        Assert.Contains(model.Requests[1], message => message.Contents.OfType<FunctionResultContent>().Any(result => result.Result?.ToString() == "A-1001: shipped, 42.50."));
    }

    [Fact]
    public async Task WithoutAModelServer_ExitsWithOne_AndSaysWhereToLook() =>
        await SampleChecks.AssertExitsNamingModelsFile(Sample.RunAsync);
}
