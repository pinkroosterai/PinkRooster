using PinkRooster.Agents.Briefs;
using PinkRooster.Agents.Prompts;
using PinkRooster.ToolCollections;

namespace PinkRooster.Agents.Tests.Briefs;

public sealed class AgentBuilderBriefTests
{
    private static readonly AgentDefaults Defaults = new(["default instruction"], ["default constraint"]);

    [Fact]
    public void WithBrief_SendsTheSameSystemPromptAsTheSectionMethods()
    {
        AgentBrief brief = new(
            role: "You review pull requests.",
            objective: "Find bugs before they merge.",
            background: "The repo is a .NET library.\nWarnings are errors.",
            instructions: ["Quote the line you mean.", "Be brief."],
            constraints: ["Never approve a change you have not read."],
            outputFormat: "A numbered list, most severe first.",
            examples: [new AgentExample("Review #12", "1. A null check is missing.\n2. Nit: rename x.")]);

        string fromBrief = Prompt(Builder().WithBrief(brief));
        string fromCode = Prompt(Builder()
            .WithRole("You review pull requests.")
            .WithObjective("Find bugs before they merge.")
            .WithBackground("The repo is a .NET library.\nWarnings are errors.")
            .WithInstructions(["Quote the line you mean.", "Be brief."])
            .WithConstraint("Never approve a change you have not read.")
            .WithOutputFormat("A numbered list, most severe first.")
            .WithExample("Review #12", "1. A null check is missing.\n2. Nit: rename x."));

        Assert.Equal(fromCode, fromBrief);
    }

    [Fact]
    public void WithBrief_ARawSystemPromptIsSentAsGiven()
    {
        Assert.Equal("Line one.\nLine two.", Prompt(Builder().WithBrief(new AgentBrief(systemPrompt: "Line one.\nLine two."))));
    }

    [Fact]
    public void ASingleValueSection_TheLastCallWins_WhateverSetItBefore()
    {
        AgentBrief brief = new(role: "from brief", objective: "brief objective");

        string codeAfter = Prompt(Builder().WithBrief(brief).WithRole("from code"));
        string codeBefore = Prompt(Builder().WithRole("from code").WithBrief(brief));
        string twoBriefs = Prompt(Builder().WithBrief(brief).WithBrief(new AgentBrief(role: "second brief")));

        Assert.Contains("# Role\nfrom code\n", codeAfter);
        Assert.Contains("# Objective\nbrief objective\n", codeAfter);
        Assert.Contains("# Role\nfrom brief\n", codeBefore);
        Assert.Contains("# Role\nsecond brief\n", twoBriefs);
        Assert.DoesNotContain("from brief", twoBriefs);
    }

    [Fact]
    public void Lists_AreAddedInCallOrderAcrossBriefAndCode_AndARepeatedLineIsSentOnce()
    {
        AgentBrief shared = new(constraints: ["Be kind.", "Stay on topic."]);
        AgentBrief own = new(role: "r", constraints: ["Stay on topic.", "Be short."]);

        string prompt = Prompt(Builder().WithConstraint("First.").WithBrief(shared).WithBrief(own).WithConstraint("Last."));

        Assert.Contains(
            "# Constraints\n- default constraint\n- First.\n- Be kind.\n- Stay on topic.\n- Be short.\n- Last.",
            prompt);
    }

    [Fact]
    public void ABriefWithOnlyConstraints_NeedsARoleFromAnotherCallBeforeBuild()
    {
        AgentBrief houseRules = new(constraints: ["Be kind."]);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => Prompt(Builder().WithBrief(houseRules)));
        string prompt = Prompt(Builder().WithBrief(houseRules).WithRole("r"));

        Assert.Contains("WithRole", error.Message);
        Assert.Contains("- Be kind.", prompt);
    }

    [Fact]
    public void ARawPromptFromABrief_WithASectionFromAnotherCall_ThrowsAtBuildAndNamesTheBrief()
    {
        AgentBuilder builder = Builder().WithBrief(new AgentBrief(systemPrompt: "raw")).WithRole("r");

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => builder.BuildOptions());

        Assert.Contains("use one or the other", error.Message);
        Assert.Contains("Briefs applied: 'code'.", error.Message);
    }

    [Fact]
    public void ABriefsSections_WithAWithSystemPromptCall_ThrowAtBuildAndNameTheBrief()
    {
        AgentBuilder builder = Builder().WithSystemPrompt("raw").WithBrief(new AgentBrief(role: "r"));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => builder.BuildOptions());

        Assert.Contains("use one or the other", error.Message);
        Assert.Contains("'code'", error.Message);
    }

    [Fact]
    public void ACloneKeepsTheBrief_AndLaterChangesToEitherLeaveTheOtherAsItWas()
    {
        AgentBuilder original = Builder().WithBrief(new AgentBrief(role: "r", instructions: ["i"]));

        AgentBuilder copy = original.Clone().WithRole("other").WithInstruction("more");

        Assert.Equal(Prompt(Builder().WithRole("r").WithInstruction("i")), Prompt(original));
        Assert.Contains("# Role\nother\n", Prompt(copy));
    }

    [Fact]
    public void OneBriefCanBeGivenToManyBuilders()
    {
        AgentBrief brief = new(role: "r", instructions: ["i"]);

        Assert.Equal(Prompt(Builder().WithBrief(brief)), Prompt(Builder().WithBrief(brief)));
    }

    [Fact]
    public void WithBrief_ANullBriefThrows() => Assert.Throws<ArgumentNullException>(() => Builder().WithBrief(null!));

    [Fact]
    public void Defaults_StillComeFirst_AndACollectionsTextComesBetweenThemAndTheBriefs()
    {
        AgentBrief brief = new(role: "r", instructions: ["from the brief"]);

        string prompt = Prompt(Builder().WithBrief(brief).WithTools(new NoteTools()));

        Assert.Contains("# Instructions\n- default instruction\n- Notes are plain text.\n- from the brief\n", prompt);
    }

    private static AgentBuilder Builder() => new RecordingChatClient(_ => "reply").CreateAgent();

    private static string Prompt(AgentBuilder builder) => builder.WithDefaults(Defaults).BuildOptions().ChatOptions!.Instructions!.Replace("\r\n", "\n");

    [ToolCollectionInstruction("Notes are plain text.")]
    private sealed class NoteTools : ToolCollection
    {
        [Tool("Reads the note. It returns plain text. It changes nothing.")]
        public string ReadNote() => "note";
    }
}
