using PinkRooster.Agents.Briefs;

namespace PinkRooster.Agents.Tests.Briefs;

public sealed class AgentBriefTests
{
    private static AgentBrief FullBrief() => new(
        role: "You review pull requests.",
        objective: "Find bugs.",
        background: "A .NET library.\nWarnings are errors.",
        instructions: ["Quote the line.", "Say why."],
        constraints: ["Never approve unread code."],
        outputFormat: "A numbered list.",
        examples: [new AgentExample("Review #12", "1. A null check is missing.")]);

    [Fact]
    public void CodeBrief_HasSourceCode_AndTheGivenSections()
    {
        AgentBrief brief = FullBrief();

        Assert.Equal("code", brief.Source);
        Assert.Equal("You review pull requests.", brief.Role);
        Assert.Equal(["Quote the line.", "Say why."], brief.Instructions);
        Assert.Equal([new AgentExample("Review #12", "1. A null check is missing.")], brief.Examples);
    }

    [Fact]
    public void NewBriefWithNoArguments_IsEmpty()
    {
        AgentBrief empty = new();

        Assert.Null(empty.Role);
        Assert.Null(empty.SystemPrompt);
        Assert.Empty(empty.Instructions);
        Assert.Empty(empty.Constraints);
        Assert.Empty(empty.Examples);
    }

    [Theory]
    [InlineData("role")]
    [InlineData("objective")]
    [InlineData("instructions")]
    [InlineData("examples")]
    [InlineData("systemPrompt")]
    public void CodeBrief_WithBlankText_ThrowsNamingTheParameter(string parameter)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => parameter switch
        {
            "role" => new AgentBrief(role: " "),
            "objective" => new AgentBrief(objective: ""),
            "instructions" => new AgentBrief(instructions: ["fine", " "]),
            "examples" => new AgentBrief(examples: [new AgentExample("in", "")]),
            _ => new AgentBrief(systemPrompt: "\t")
        });

        Assert.Equal(parameter, error.ParamName);
        Assert.Contains(parameter, error.Message);
    }

    [Fact]
    public void CodeBrief_WithSystemPromptAndASection_ThrowsNamingTheFix()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new AgentBrief(role: "r", systemPrompt: "all of it"));

        Assert.Equal("systemPrompt", error.ParamName);
        Assert.Contains("remove systemPrompt or the sections", error.Message);
    }
}
