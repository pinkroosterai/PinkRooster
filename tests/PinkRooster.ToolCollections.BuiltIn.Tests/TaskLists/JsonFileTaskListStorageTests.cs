using PinkRooster.ToolCollections.BuiltIn.TaskLists;

namespace PinkRooster.ToolCollections.BuiltIn.Tests.TaskLists;

public sealed class JsonFileTaskListStorageTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pinkrooster-tasklist-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(directory, "nested", "tasks.json");

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_NoFile_IsNull()
    {
        Assert.Null(await new JsonFileTaskListStorage(FilePath).LoadAsync(default));
    }

    [Fact]
    public async Task SaveAsyncThenLoadAsync_RoundTripsTheList_AndCreatesTheDirectory()
    {
        JsonFileTaskListStorage storage = new(FilePath);
        TaskListSnapshot snapshot = new()
        {
            NextId = 4,
            Items =
            [
                new TaskItem { Id = 1, Subject = "A", Status = TaskItemStatus.Completed },
                new TaskItem { Id = 3, Subject = "B", Description = "detail", Status = TaskItemStatus.InProgress, BlockedBy = [1] }
            ]
        };

        await storage.SaveAsync(snapshot, default);
        TaskListSnapshot? loaded = await new JsonFileTaskListStorage(FilePath).LoadAsync(default);

        Assert.NotNull(loaded);
        Assert.Equal(4, loaded.NextId);
        Assert.Equal([1, 3], loaded.Items.Select(item => item.Id));
        Assert.Equal(TaskItemStatus.InProgress, loaded.Items[1].Status);
        Assert.Equal("detail", loaded.Items[1].Description);
        Assert.Equal([1], loaded.Items[1].BlockedBy);
    }

    [Fact]
    public async Task SaveAsync_WritesReadableJsonWithStatusNames_AndLeavesNoTemporaryFile()
    {
        JsonFileTaskListStorage storage = new(FilePath);

        await storage.SaveAsync(new TaskListSnapshot { NextId = 2, Items = [new TaskItem { Id = 1, Subject = "A", Status = TaskItemStatus.InProgress }] }, default);

        string json = await File.ReadAllTextAsync(FilePath);
        Assert.Contains("\"status\": \"in_progress\"", json);
        Assert.Contains("\"nextId\": 2", json);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public async Task SaveAsync_ReplacesAnExistingFile()
    {
        JsonFileTaskListStorage storage = new(FilePath);
        await storage.SaveAsync(new TaskListSnapshot { NextId = 2, Items = [new TaskItem { Id = 1, Subject = "Old" }] }, default);

        await storage.SaveAsync(new TaskListSnapshot { NextId = 3, Items = [new TaskItem { Id = 2, Subject = "New" }] }, default);

        TaskListSnapshot loaded = (await storage.LoadAsync(default))!;
        Assert.Equal("New", Assert.Single(loaded.Items).Subject);
    }

    [Fact]
    public async Task LoadAsync_CorruptFile_IsMovedAsideAndTheListStartsEmpty()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        await File.WriteAllTextAsync(FilePath, "{ this is not json");

        TaskListSnapshot? loaded = await new JsonFileTaskListStorage(FilePath).LoadAsync(default);

        Assert.Null(loaded);
        Assert.False(File.Exists(FilePath));
        Assert.Equal("{ this is not json", await File.ReadAllTextAsync(FilePath + ".corrupt"));
    }

    [Fact]
    public async Task WithACollection_TheListSurvivesANewCollectionOnTheSameFile()
    {
        TaskListToolCollection first = new(new JsonFileTaskListStorage(FilePath));
        await first.TaskAdd([new("A"), new("B")]);
        await first.TaskUpdate([new(1, TaskItemStatus.Completed)]);

        TaskListToolCollection second = new(new JsonFileTaskListStorage(FilePath));

        Assert.Equal("## Task list (1/2 done)\n#2 [pending] B\nCompleted: 1.", await second.GetContextAsync(default));
    }
}
