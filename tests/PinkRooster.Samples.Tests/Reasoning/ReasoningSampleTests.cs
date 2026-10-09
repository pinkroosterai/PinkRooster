using Microsoft.Extensions.AI;
using PinkRooster.Agents.Tests.TestSupport;
using PinkRooster.Samples.Tests.TestSupport;
using static PinkRooster.Samples.Tests.TestSupport.Replies;
using Sample = PinkRooster.Samples.Reasoning.Program;

namespace PinkRooster.Samples.Tests.Reasoning;

public sealed class ReasoningSampleTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheReasoningIsRequestedInFull_AndShownBeforeTheAnswer()
    {
        ScriptedChatClient model = new(new ChatMessage(ChatRole.Assistant, [new TextReasoningContent("If the ball is x, the bat is x + 1.00."), new TextContent("The ball costs 0.05.")]));
        RecordingConsole console = new();

        await Sample.RunAsync(model, console, Token);

        Assert.Equal(ReasoningOutput.Full, model.Options[0]!.Reasoning!.Output);
        Assert.Equal("Reasoning: If the ball is x, the bat is x + 1.00.\nAnswer: The ball costs 0.05.\n", console.Output);
    }

    [Fact]
    public async Task AModelThatSendsNoReasoning_ShowsOnlyTheAnswer()
    {
        ScriptedChatClient model = new(Text("The ball costs 0.05."));
        RecordingConsole console = new();

        await Sample.RunAsync(model, console, Token);

        Assert.Equal("Answer: The ball costs 0.05.\n", console.Output);
    }

    [Fact]
    public async Task WithoutAModelServer_ExitsWithOne_AndSaysWhereToLook() =>
        await SampleChecks.AssertExitsNamingModelsFile(Sample.RunAsync);
}
