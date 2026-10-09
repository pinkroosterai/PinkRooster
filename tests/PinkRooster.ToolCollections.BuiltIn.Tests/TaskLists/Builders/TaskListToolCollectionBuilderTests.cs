using PinkRooster.ToolCollections.BuiltIn.TaskLists;
using PinkRooster.ToolCollections.BuiltIn.Tests.TestSupport;

namespace PinkRooster.ToolCollections.BuiltIn.Tests.TaskLists;

public sealed class TaskListToolCollectionBuilderTests : IDisposable
{
    private readonly TempDirectory temp = new("tasklistbuilder-test-");

    public void Dispose() => temp.Dispose();

    [Fact]
    public void Build_WithoutSettings_GivesTheSameInstructionsAsTheConstructor()
    {
        TaskListToolCollection built = new TaskListToolCollectionBuilder().Build();

        Assert.Equal(new TaskListToolCollection(new InMemoryTaskListStorage()).Instructions, built.Instructions);
    }

    [Fact]
    public void Build_WithPerSessionAndAStorage_ThrowsNamingTheFix()
    {
        InvalidOperationException fromStorage = Assert.Throws<InvalidOperationException>(
            () => new TaskListToolCollectionBuilder().PerSession().WithStorage(new InMemoryTaskListStorage()).Build());
        InvalidOperationException fromFile = Assert.Throws<InvalidOperationException>(
            () => new TaskListToolCollectionBuilder().WithJsonFile(Path.Combine(temp.Path, "tasks.json")).PerSession().Build());

        Assert.Contains("Remove PerSession()", fromStorage.Message);
        Assert.Contains("Remove PerSession()", fromFile.Message);
    }

    [Fact]
    public void Build_PutsTheSwitchesIntoTheCollection()
    {
        TaskListToolCollection built = new TaskListToolCollectionBuilder().AllowMultipleInProgress().WithoutContext().Build();

        Assert.Contains(built.Instructions, text => text.Contains("mark a task in_progress when you start it"));
        Assert.Contains(built.Instructions, text => text.Contains("not shown automatically"));
    }

    [Fact]
    public async Task Build_WithAJsonFile_KeepsTheListInTheFile()
    {
        string path = Path.Combine(temp.Path, "tasks.json");
        TaskListToolCollection first = new TaskListToolCollectionBuilder().WithJsonFile(path).Build();
        await first.TaskAdd([new TaskAddInput("A")]);

        Assert.True(File.Exists(path));
        string second = await new TaskListToolCollectionBuilder().WithJsonFile(path).Build().TaskGet(null);
        Assert.Contains("A", second);
    }

    [Fact]
    public void Build_CalledTwice_GivesIndependentCollections()
    {
        TaskListToolCollectionBuilder builder = new();

        Assert.NotSame(builder.Build(), builder.Build());
    }

    [Fact]
    public void Methods_RefuseBadArgumentsAtTheCall()
    {
        TaskListToolCollectionBuilder builder = new();

        Assert.Throws<ArgumentNullException>(() => builder.WithStorage(null!));
        Assert.Throws<ArgumentException>(() => builder.WithJsonFile(" "));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithMaxVisibleItems(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithMaxSubjectLength(9));
        Assert.Throws<ArgumentNullException>(() => builder.FormatContextWith(null!));
        Assert.Throws<ArgumentNullException>(() => builder.WithLogger(null!));
    }
}
