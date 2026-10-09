using Microsoft.Extensions.AI;
using PinkRooster.Agents.Tests.TestSupport;
using PinkRooster.Samples.Tests.TestSupport;
using static PinkRooster.Samples.Tests.TestSupport.Replies;
using Sample = PinkRooster.Samples.SessionState.Program;

namespace PinkRooster.Samples.Tests.SessionState;

public sealed class SessionStateSampleTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EachSessionKeepsItsOwnNotes()
    {
        ScriptedChatClient model = new(
            Call("c1", "AddNote", new() { ["text"] = "likes tea" }), Text("Noted."),
            Call("c2", "AddNote", new() { ["text"] = "likes coffee" }), Text("Noted."),
            Text("You like tea."));
        RecordingConsole console = new();

        await Sample.RunAsync(model, console, Token);

        Assert.Contains("The first session holds: likes tea\n", console.Output);
        Assert.Contains("The second session holds: likes coffee\n", console.Output);
        string lastRequest = string.Join("\n", model.Requests[4].Select(message => message.Text));
        Assert.Contains("likes tea", lastRequest);
        Assert.DoesNotContain("likes coffee", lastRequest);
    }

    [Fact]
    public async Task WithoutAModelServer_ExitsWithOne_AndSaysWhereToLook() =>
        await SampleChecks.AssertExitsNamingModelsFile(Sample.RunAsync);
}
