using Microsoft.Extensions.AI;
using PinkRooster.Agents.Tests.TestSupport;
using PinkRooster.Samples.Tests.TestSupport;
using static PinkRooster.Samples.Tests.TestSupport.Replies;
using Sample = PinkRooster.Samples.Builder.Program;

namespace PinkRooster.Samples.Tests.Builder;

public sealed class BuilderSampleTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ShowsTheComposedPrompt_AndRunsTheStrictVariant()
    {
        ScriptedChatClient model = new(Text("1. Add a test."));
        RecordingConsole console = new();

        await Sample.RunAsync(model, console, Token);

        string prompt = Assert.Single(model.Options)!.Instructions!;
        Assert.Contains("Block any change that has no test.", prompt);
        Assert.Contains("Answer in plain English.", prompt);
        Assert.True(prompt.IndexOf("You review pull requests.", StringComparison.Ordinal) < prompt.IndexOf("Find bugs before they merge.", StringComparison.Ordinal));
        Assert.Contains("The system prompt:", console.Output);
        Assert.Contains("You review pull requests.", console.Output);
        Assert.DoesNotContain("Block any change", console.Output);
        Assert.Contains("1. Add a test.", console.Output);
        Assert.Contains("Without defaults:", console.Output);
    }

    [Fact]
    public async Task WithoutAModelServer_ExitsWithOne_AndSaysWhereToLook() =>
        await SampleChecks.AssertExitsNamingModelsFile(Sample.RunAsync);
}
