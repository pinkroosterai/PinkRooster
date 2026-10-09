using Microsoft.Extensions.AI;
using PinkRooster.Agents.Tests.TestSupport;
using PinkRooster.Samples.Tests.TestSupport;
using static PinkRooster.Samples.Tests.TestSupport.Replies;
using Sample = PinkRooster.Samples.AgentClasses.Program;

namespace PinkRooster.Samples.Tests.AgentClasses;

public sealed class AgentClassesSampleTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheClassBriefsItself_RunsItsOwnTool_AndTellsItsHandlers()
    {
        ScriptedChatClient model = new(Call("c1", "CountLines", new() { ["text"] = "a\nb\nc" }), Text("1. `log(user)` runs after a null check."));
        RecordingConsole console = new();

        await Sample.RunAsync(model, console, Token);

        string prompt = model.Options[0]!.Instructions!;
        Assert.Contains("You review pull requests.", prompt);
        Assert.Contains("Quote the line you mean.", prompt);
        Assert.Contains("The repository is PinkRooster.", prompt);
        Assert.Contains(model.Options[0]!.Tools!, tool => tool.Name == "CountLines");
        Assert.Contains(model.Requests[0], message => message.Text.Contains("## Repository"));
        Assert.Contains("CountLines: Succeeded", console.Output);
        Assert.Contains("The class saw these tool calls: CountLines", console.Output);
    }

    [Fact]
    public async Task WithoutAModelServer_ExitsWithOne_AndSaysWhereToLook() =>
        await SampleChecks.AssertExitsNamingModelsFile(Sample.RunAsync);
}
