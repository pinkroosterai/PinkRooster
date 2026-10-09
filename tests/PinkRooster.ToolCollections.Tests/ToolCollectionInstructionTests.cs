
namespace PinkRooster.ToolCollections.Tests;

public sealed class ToolCollectionInstructionTests
{
    [Fact]
    public void Instructions_ListAttributeTextThenTextAddedInTheConstructor()
    {
        ShellLikeTools tools = new("bash");

        Assert.Equal(["Keep commands short.", "Commands run in bash."], tools.Instructions);
        Assert.Equal(["Never delete files."], tools.Constraints);
    }

    [Fact]
    public void Instructions_OfASubclassIncludeItsBaseClassAttributes()
    {
        DerivedTools tools = new();

        Assert.Contains("Keep commands short.", tools.Instructions);
        Assert.Contains("Log every command.", tools.Instructions);
    }

    [Fact]
    public void Instructions_AreEmptyForACollectionWithoutAny()
    {
        PlainTools tools = new();

        Assert.Empty(tools.Instructions);
        Assert.Empty(tools.Constraints);
    }

    [Fact]
    public void AddInstruction_RejectsBlankText()
    {
        Assert.Throws<ArgumentException>(() => new ShellLikeTools(" "));
    }

    [Fact]
    public void Instructions_WithBlankAttributeTextThrowNamingTheCollection()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => new BlankTools().Instructions);

        Assert.Contains(nameof(BlankTools), error.Message);
    }

    [ToolCollectionInstruction("Keep commands short.")]
    [ToolCollectionConstraint("Never delete files.")]
    private class ShellLikeTools : ToolCollection
    {
        public ShellLikeTools(string shell)
        {
            AddInstruction(shell == " " ? shell : $"Commands run in {shell}.");
        }

        [Tool("Runs a command. It returns the output. It never prompts.")]
        public string Run(string command) => command;
    }

    [ToolCollectionInstruction("Log every command.")]
    private sealed class DerivedTools() : ShellLikeTools("bash");

    private sealed class PlainTools : ToolCollection;

    [ToolCollectionInstruction("ok", " ")]
    private sealed class BlankTools : ToolCollection;
}
