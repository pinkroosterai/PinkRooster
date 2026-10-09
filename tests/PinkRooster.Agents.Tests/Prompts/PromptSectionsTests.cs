using PinkRooster.Agents.Briefs;
using PinkRooster.Agents.Prompts;
using PinkRooster.ToolCollections;

namespace PinkRooster.Agents.Tests.Prompts;

public sealed class PromptSectionsTests
{
    private static readonly AgentDefaults Defaults = new(["default instruction"], ["default constraint"]);

    [Fact]
    public void Compose_RendersEverySectionInOrder_WithDefaultsBeforeTheOwnLines()
    {
        PromptSections sections = new()
        {
            Role = "role",
            Objective = "objective",
            Background = "background",
            OutputFormat = "format"
        };
        sections.Instructions.Add("own instruction");
        sections.Constraints.Add("own constraint");
        sections.Examples.Add(new AgentExample("in", "out"));

        string prompt = sections.Compose(Defaults, []);

        Assert.Equal(
            string.Join(Environment.NewLine,
                "# Role", "role", "",
                "# Objective", "objective", "",
                "# Background", "background", "",
                "# Instructions", "- default instruction", "- own instruction", "",
                "# Constraints", "- default constraint", "- own constraint", "",
                "# Output Format", "format", "",
                "# Examples", "Example 1:", "Input: in", "Output: out"),
            prompt);
    }

    [Fact]
    public void Compose_KeepsEveryLineOfAMultiLineItemInsideItsBullet()
    {
        PromptSections sections = new() { Role = "role" };
        sections.Instructions.Add("line one\nline two\r\n\r\nline four\n");
        sections.Constraints.Add("single");

        string prompt = sections.Compose(AgentDefaults.None, []);

        Assert.Equal(
            string.Join(Environment.NewLine,
                "# Role", "role", "",
                "# Instructions", "- line one", "  line two", "", "  line four", "",
                "# Constraints", "- single"),
            prompt);
    }

    [Fact]
    public void Compose_PutsCollectionTextBetweenTheDefaultsAndTheOwnLines_AndSendsARepeatedLineOnce()
    {
        PromptSections sections = new() { Role = "role" };
        sections.Instructions.AddRange(["own instruction", "Notes are plain text."]);

        string prompt = sections.Compose(Defaults, [new NoteTools()]);

        Assert.Contains(
            string.Join(Environment.NewLine, "# Instructions", "- default instruction", "- Notes are plain text.", "- own instruction"),
            prompt);
    }

    [Fact]
    public void Compose_WithARawPrompt_ReturnsItAsGivenWithoutDefaultsOrCollectionText()
    {
        PromptSections sections = new() { SystemPrompt = "  exactly this  " };

        Assert.Equal("  exactly this  ", sections.Compose(Defaults, [new NoteTools()]));
    }

    [Fact]
    public void Compose_WithARawPromptAndASection_ThrowsAndNamesTheFix()
    {
        PromptSections sections = new() { SystemPrompt = "raw", Role = "role" };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => sections.Compose(Defaults, []));

        Assert.Contains("use one or the other", error.Message);
    }

    [Fact]
    public void Compose_WithNeitherARoleNorARawPrompt_ThrowsAndNamesTheFix()
    {
        PromptSections sections = new();
        sections.Instructions.Add("only an instruction");

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => sections.Compose(Defaults, []));

        Assert.Contains("WithRole", error.Message);
        Assert.Contains("WithSystemPrompt", error.Message);
    }

    [Fact]
    public void Clone_CopiesEverySection_AndChangingEitherLeavesTheOtherAsItWas()
    {
        PromptSections original = new() { Role = "role", Objective = "objective", Background = "background", OutputFormat = "format" };
        original.Instructions.Add("instruction");
        original.Constraints.Add("constraint");
        original.Examples.Add(new AgentExample("in", "out"));

        PromptSections copy = original.Clone();
        string before = original.Compose(Defaults, []);

        Assert.Equal(before, copy.Compose(Defaults, []));

        copy.Role = "other role";
        copy.Instructions.Add("more");
        copy.Examples.Clear();

        Assert.Equal(before, original.Compose(Defaults, []));
    }

    [ToolCollectionInstruction("Notes are plain text.")]
    private sealed class NoteTools : ToolCollection
    {
        [Tool("Reads the note. It returns plain text. It changes nothing.")]
        public string ReadNote() => "note";
    }
}
