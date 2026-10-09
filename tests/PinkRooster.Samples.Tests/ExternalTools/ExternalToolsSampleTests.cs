using Microsoft.Extensions.AI;
using PinkRooster.Agents.Tests.TestSupport;
using PinkRooster.Samples.Tests.TestSupport;
using static PinkRooster.Samples.Tests.TestSupport.Replies;
using Sample = PinkRooster.Samples.ExternalTools.Program;

namespace PinkRooster.Samples.Tests.ExternalTools;

public sealed class ExternalToolsSampleTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheCollectionsTextAndContextReachTheModel()
    {
        ScriptedChatClient model = new(Call("c1", "GetWeather", new() { ["city"] = "Utrecht" }), Text("Light rain, 14 degrees."));
        RecordingConsole console = new();

        await Sample.RunAsync(model, console, Token);

        Assert.Contains("Temperatures are in Celsius.", model.Options[0]!.Instructions);
        Assert.Contains(model.Requests[0], message => message.Text.Contains("## Sensors\nOnline: 3"));
        Assert.Contains(model.Requests[1], message => message.Contents.OfType<FunctionResultContent>().Any(result => result.Result?.ToString() == "Utrecht: 14 degrees, light rain."));
        Assert.Contains("Light rain, 14 degrees.", console.Output);
    }

    [Fact]
    public async Task AToolNeedingApproval_EndsTheRunWithARequest_AndTheSampleSaysSo()
    {
        ScriptedChatClient model = new(Call("c1", "RestartSensor"));
        RecordingConsole console = new();

        await Sample.RunAsync(model, console, Token);

        Assert.Contains("RestartSensor needs approval", console.Output);
    }

    [Fact]
    public async Task WithoutAModelServer_ExitsWithOne_AndSaysWhereToLook() =>
        await SampleChecks.AssertExitsNamingModelsFile(Sample.RunAsync);
}
