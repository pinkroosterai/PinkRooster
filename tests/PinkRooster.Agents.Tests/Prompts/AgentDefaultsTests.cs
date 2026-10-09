using Microsoft.Agents.AI;
using PinkRooster.Agents.Prompts;
using PinkRooster.ToolCollections;

namespace PinkRooster.Agents.Tests.Prompts;

public sealed class AgentDefaultsTests
{
    [Fact]
    public void BuiltIn_HoldsTheShippedThreeInstructionsAndThreeConstraints()
    {
        Assert.Equal(3, AgentDefaults.BuiltIn.Instructions.Count);
        Assert.Equal(3, AgentDefaults.BuiltIn.Constraints.Count);
        Assert.Contains("Never invent a tool's result; if a tool fails, say so.", AgentDefaults.BuiltIn.Constraints);
    }

    [Fact]
    public void AgentDefaults_HasNoProcessWideCurrent()
    {
        Assert.Null(typeof(AgentDefaults).GetProperty("Current"));
    }

    [Fact]
    public async Task Build_WithOnlyARoleSendsTheBuiltInDefaults()
    {
        RecordingChatClient client = new(_ => "reply");

        await client.CreateAgent().WithRole("r").Build().RunAsync("go");

        string prompt = client.Requests[0].Instructions!;
        Assert.All(AgentDefaults.BuiltIn.Instructions, instruction => Assert.Contains($"- {instruction}", prompt));
        Assert.All(AgentDefaults.BuiltIn.Constraints, constraint => Assert.Contains($"- {constraint}", prompt));
    }

    [Fact]
    public async Task WithoutDefaults_LeavesOutOnlyTheDefaults()
    {
        RecordingChatClient client = new(_ => "reply");

        await client.CreateAgent().WithoutDefaults().WithRole("r").WithTools(new NoteTools()).Build().RunAsync("go");

        string prompt = client.Requests[0].Instructions!;
        Assert.DoesNotContain(AgentDefaults.BuiltIn.Instructions[0], prompt);
        Assert.Contains("- Notes are plain text.", prompt);
    }

    [Fact]
    public async Task Clone_KeepsWithDefaults()
    {
        RecordingChatClient client = new(_ => "reply");

        await client.CreateAgent().WithDefaults(new AgentDefaults(["Mine."], [])).WithRole("r").Clone().Build().RunAsync("go");

        Assert.Contains("- Mine.", client.Requests[0].Instructions);
    }

    [Fact]
    public async Task WithSystemPrompt_SendsNoDefaults()
    {
        RecordingChatClient client = new(_ => "reply");

        await client.CreateAgent().WithSystemPrompt("x").Build().RunAsync("go");

        Assert.Equal("x", client.Requests[0].Instructions);
    }

    [Fact]
    public async Task EmptyDefaults_AddNoSections()
    {
        RecordingChatClient client = new(_ => "reply");

        await client.CreateAgent().WithDefaults(AgentDefaults.None).WithRole("r").Build().RunAsync("go");

        Assert.Equal("# Role\nr", client.Requests[0].Instructions!.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Constructor_CopiesTheLists()
    {
        List<string> instructions = ["a"];
        AgentDefaults defaults = new(instructions, []);

        instructions.Add("b");

        Assert.Equal(["a"], defaults.Instructions);
    }

    [Fact]
    public void Constructor_RejectsNullAndBlankEntries()
    {
        Assert.Throws<ArgumentNullException>(() => new AgentDefaults(null!, []));
        Assert.Equal("constraints", Assert.Throws<ArgumentException>(() => new AgentDefaults([], ["ok", " "])).ParamName);
    }

    [ToolCollectionInstruction("Notes are plain text.")]
    private sealed class NoteTools : ToolCollection
    {
        [Tool("Reads the note. It returns plain text. It changes nothing.")]
        public string ReadNote() => "note";
    }
}
