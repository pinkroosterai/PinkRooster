using Microsoft.Extensions.AI;
using PinkRooster.Agents.Tests.TestSupport;
using static PinkRooster.Samples.Tests.CodingAgent.Showcase;

namespace PinkRooster.Samples.Tests.CodingAgent;

/// <summary>The image tool: on when the model settings mark a model that can see, and the images go to that model.</summary>
public sealed class ImageToolTests
{
    private static string Settings(params (string Model, bool Vision)[] models) =>
        "{ \"models\": [" + string.Join(", ", models.Select(model =>
            $"{{ \"model\": \"{model.Model}\", \"endpoint\": \"http://localhost:1/v1\", \"vision\": {(model.Vision ? "true" : "false")} }}")) + "] }";

    private static ChatMessage Look(string path, string question) =>
        Call("QueryImage", ("paths", new[] { path }), ("question", question));

    // A real image for the workspace: the chart the Imaging sample ships, which the build copies next to this test.
    private static void PutChart(Showcase showcase, string relativePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(showcase.PathOf(relativePath))!);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "images", "sales-chart.png"), showcase.PathOf(relativePath));
    }

    [Fact]
    public async Task WithAModelMarkedVision_TheAssistantLooksThroughThatModel_AndOnlyTextComesBack()
    {
        using Showcase showcase = Showcase.WithSettings(Settings(("main", false), ("eyes", true)));
        PutChart(showcase, "docs/chart.png");
        ScriptedChatClient eyes = new(Text("A bar chart; the third bar is the tallest."));
        showcase.Use("eyes", eyes);
        ScriptedChatClient model = new(Look("docs/chart.png", "What does this chart show?"), Text("It is a bar chart."));

        await showcase.RunAsync(model, "what is in docs/chart.png?", "/status");

        Assert.Contains(model.Options[0]!.Tools!, tool => tool.Name == "QueryImage");
        // Looking changes nothing, so it runs unasked in the default mode.
        Assert.Empty(showcase.Console.ApprovalsAsked);
        // The image went to the model that can see, and the assistant's own model was sent text only.
        List<ChatMessage> looked = Assert.Single(eyes.Requests);
        Assert.Single(looked.SelectMany(message => message.Contents).OfType<DataContent>());
        Assert.DoesNotContain(model.Requests.SelectMany(request => request).SelectMany(message => message.Contents), content => content is DataContent);
        Assert.Equal("A bar chart; the third bar is the tallest.", Assert.Single(Results(model)).Result);
        Assert.Contains("Image tool: on eyes.", showcase.Console.Output);
    }

    [Fact]
    public async Task WhenTheAssistantsOwnModelIsMarked_ItLooksItself()
    {
        using Showcase showcase = Showcase.WithSettings(Settings(("solo", true)));
        PutChart(showcase, "chart.png");
        // One model plays both parts: it asks for the look, answers the look, and then answers the user.
        ScriptedChatClient model = new(Look("chart.png", "What is this?"), Text("A bar chart."), Text("The file is a bar chart."));

        await showcase.RunAsync(model, "what is chart.png?");

        Assert.Equal(3, model.Requests.Count);
        Assert.Single(model.Requests[1].SelectMany(message => message.Contents).OfType<DataContent>());
        Assert.Contains("Image tool: on solo.", showcase.Console.Output);
    }

    [Fact]
    public async Task WithoutAModelMarkedVision_ThereIsNoImageTool_AndStatusSaysHowToTurnItOn()
    {
        using Showcase showcase = new();
        ScriptedChatClient model = new(Text("ok"));

        await showcase.RunAsync(model, "hello", "/status");

        Assert.DoesNotContain(model.Options[0]!.Tools!, tool => tool.Name == "QueryImage");
        Assert.DoesNotContain("Image tool: on", showcase.Console.Output);
        Assert.Contains("Image tool: off. Mark a model \"vision\": true in", showcase.Console.Output);
    }

    [Fact]
    public async Task AnImageOutsideTheWorkspace_IsRefusedLikeAFileOutsideIt()
    {
        using Showcase showcase = Showcase.WithSettings(Settings(("solo", true)));
        ScriptedChatClient model = new(Look("../elsewhere.png", "What is this?"), Text("I cannot reach that file."));

        await showcase.RunAsync(model, "look at ../elsewhere.png");

        Assert.StartsWith("Error:", Assert.Single(Results(model)).Result);
        // Only the assistant's two requests: nothing was sent to look at.
        Assert.Equal(2, model.Requests.Count);
    }
}
