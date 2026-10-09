using Microsoft.Extensions.AI;
using PinkRooster.Agents.Eventing;
using PinkRooster.Agents.Tests.TestSupport;
using static PinkRooster.Samples.Tests.CodingAgent.Showcase;

namespace PinkRooster.Samples.Tests.CodingAgent;

/// <summary>Helpers on their own model and tool set, and tools that run in the background.</summary>
public sealed class HelperAndBackgroundTests
{
    private static ChatMessage Helper(string model, string prompt, params string[] toolSets) =>
        Call("RunSubAgent", ("modelName", model), ("subAgentName", "Finder"), ("role", "You find things in code."), ("prompt", prompt), ("toolSets", toolSets));

    [Fact]
    public async Task AHelper_RunsOnTheModelItWasGiven_WithTheReadSetOnly()
    {
        using Showcase showcase = new("main-model", "small-model");
        ScriptedChatClient small = new(Text("it is in a.txt"));
        showcase.Use("small-model", small);
        ScriptedChatClient main = new(Helper("small-model", "Find the parser.", "read"), Text("the parser is in a.txt"));

        await showcase.RunAsync(main, "where is the parser?");

        // The model question was answered with the first model, which the assistant runs on.
        Assert.Equal("Which model should the assistant run on?", Assert.Single(showcase.Console.QuestionsAsked).Question);
        Assert.Equal(["FindFiles", "ListDirectory", "ReadFile", "SearchFiles"], small.Options[0]!.Tools!.Select(tool => tool.Name).Order(StringComparer.Ordinal));
        Assert.Contains(small.Requests[0], message => message.Text == "Find the parser.");
        Assert.Equal("it is in a.txt", Assert.Single(Results(main)).Result);
        // Both models are offered to the assistant for its helpers, each with its use-when line.
        Assert.Contains("For small-model work.", main.Options[0]!.Instructions);
    }

    [Fact]
    public async Task AHelpersCommand_IsAnsweredByTheSamePolicy()
    {
        using Showcase showcase = new("main-model", "small-model");
        ScriptedChatClient small = new(Shell("echo from the helper"), Text("ran it"), Shell("echo again"), Text("refused"));
        showcase.Use("small-model", small);
        ScriptedChatClient main = new(Helper("small-model", "Run it.", "shell"), Text("done"), Helper("small-model", "Run it again.", "shell"), Text("planned"));

        await showcase.RunAsync(main, "run it through a helper", "/mode plan", "and again");

        // In ask mode the helper's command was put to the user; in plan mode it was refused without asking.
        Assert.Equal(["RunShell"], showcase.Console.ApprovalsAsked.Select(question => question.Call.Name));
        Assert.Contains(small.Requests[^1].SelectMany(message => message.Contents).OfType<FunctionResultContent>(),
            result => (result.Result?.ToString() ?? "").Contains("permission mode is 'plan'"));
    }

    [Fact]
    public async Task ARunShellCallInTheBackground_ReturnsATaskId_AndTheRunWaitsForIt()
    {
        using Showcase showcase = new();
        // The command must outlast the model's second call: a task that ends before it is reported in that call, and nothing wakes the model.
        // "sleep" is a command in bash and an alias in PowerShell.
        ScriptedChatClient model = new(Shell("sleep 1; echo slow build", inBackground: true), Text("started it"), Text("the build is done"));

        await showcase.RunAsync(model, "build it");

        Assert.Equal(["RunShell"], showcase.Console.ApprovalsAsked.Select(question => question.Call.Name));
        Assert.StartsWith("Started task 1 (RunShell).", Results(model).First(result => result.Name == "RunShell").Result);
        BackgroundTaskEnded ended = Assert.Single(showcase.Console.Events.OfType<BackgroundTaskEnded>());
        Assert.Equal(BackgroundTaskState.Completed, ended.State);
        // The task's end woke the model once more.
        Assert.Equal(3, model.Requests.Count);
        Assert.Contains(model.Options[0]!.Tools!, tool => tool.Name == "WaitForTasks");
    }
}
