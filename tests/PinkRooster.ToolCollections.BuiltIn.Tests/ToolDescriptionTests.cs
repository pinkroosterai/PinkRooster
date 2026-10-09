using System.Text.Json;
using Microsoft.Extensions.AI;
using PinkRooster.ToolCollections.BuiltIn.AskUserQuestion;
using PinkRooster.ToolCollections.BuiltIn.DateTimes;
using PinkRooster.ToolCollections.BuiltIn.FileOperations;
using PinkRooster.ToolCollections.BuiltIn.Images;
using PinkRooster.ToolCollections.BuiltIn.Shared;
using PinkRooster.ToolCollections.BuiltIn.Shells;
using PinkRooster.ToolCollections.BuiltIn.TaskLists;

namespace PinkRooster.ToolCollections.BuiltIn.Tests;

/// <summary>Keeps the ready-made tools' descriptions useful to select by: they are long enough to select by, name the sibling tools they could be mistaken for, and describe every parameter.</summary>
public sealed class ToolDescriptionTests
{
    public static TheoryData<string, AIFunction> ReadyMadeTools()
    {
        TheoryData<string, AIFunction> data = [];
        ToolCollection[] collections =
        [
            new AskUserQuestionToolCollection((_, _) => Task.FromResult<IReadOnlyList<UserQuestionAnswer>>([])),
            new ShellToolCollection(),
            new DateTimeToolCollection(),
            new FileOperationsToolCollection(new Workspace(Directory.GetCurrentDirectory())),
            new FileReadToolCollection(new Workspace(Directory.GetCurrentDirectory())),
            new TaskListToolCollection(new InMemoryTaskListStorage()),
            new ImageToolCollection(new Workspace(Directory.GetCurrentDirectory()), new TestSupport.UnansweringChatClient())
        ];
        foreach (AIFunction function in collections.SelectMany(collection => collection.GetAIFunctions()))
        {
            data.Add(function.Name, function);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(ReadyMadeTools))]
    public void Tool_HasADescriptionLongEnoughToSelectBy(string name, AIFunction function)
    {
        Assert.True(function.Description.Length >= 60, $"{name} has a description too short to select it by: '{function.Description}'");
    }

    // Where two tools can be mistaken for each other, each names the other so the model picks by intent.
    [Theory]
    [InlineData("CreateFile", "EditFile")]
    [InlineData("CreateFile", "WriteFile")]
    [InlineData("WriteFile", "EditFile")]
    [InlineData("WriteFile", "CreateFile")]
    [InlineData("EditFile", "WriteFile")]
    [InlineData("EditFile", "ReadFile")]
    [InlineData("ReadFile", "EditFile")]
    [InlineData("FindFiles", "SearchFiles")]
    [InlineData("FindFiles", "ListDirectory")]
    [InlineData("SearchFiles", "FindFiles")]
    public void FileTool_NamesTheSiblingItCouldBeMistakenFor(string tool, string sibling)
    {
        AIFunction function = new FileOperationsToolCollection(new Workspace(Directory.GetCurrentDirectory())).GetAIFunctions().Single(f => f.Name == tool);

        Assert.Contains(sibling, function.Description);
    }

    [Theory]
    [MemberData(nameof(ReadyMadeTools))]
    public void Tool_DescribesEveryParameter(string name, AIFunction function)
    {
        if (!function.JsonSchema.TryGetProperty("properties", out JsonElement properties))
        {
            return;
        }

        foreach (JsonProperty parameter in properties.EnumerateObject())
        {
            Assert.True(parameter.Value.TryGetProperty("description", out JsonElement description) && description.GetString()?.Length > 10,
                $"{name}.{parameter.Name} has no description.");
        }
    }
}
