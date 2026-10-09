using Microsoft.Extensions.AI;
using PinkRooster.ToolCollections.BuiltIn.TaskLists;

namespace PinkRooster.ToolCollections.BuiltIn.Tests.TaskLists;

public sealed class TaskListToolCollectionTests
{
    private static TaskListToolCollection Create(TaskListOptions? options = null, InMemoryTaskListStorage? storage = null) =>
        new(storage ?? new InMemoryTaskListStorage(), options);

    private static TaskAddInput[] Add(params string[] subjects) => [.. subjects.Select(subject => new TaskAddInput(subject))];

    private static TaskUpdateInput Set(int id, TaskItemStatus status) => new(id, status);

    private static async Task<TaskListToolCollection> CreateWithAsync(params string[] subjects)
    {
        TaskListToolCollection collection = Create();
        await collection.TaskAdd(Add(subjects));
        return collection;
    }

    // ---- TaskAdd ----

    [Fact]
    public async Task TaskAdd_GivesSequentialIdsAndPointsAtTheFirstTaskToStart()
    {
        TaskListToolCollection collection = Create();

        string result = await collection.TaskAdd(Add("Design", "Build", "Ship"));

        Assert.Equal("Added #1,#2,#3. Next: #1.", result);
    }

    [Fact]
    public async Task TaskAdd_LaterCallsContinueTheNumbering()
    {
        TaskListToolCollection collection = await CreateWithAsync("One", "Two");

        string result = await collection.TaskAdd(Add("Three"));

        Assert.StartsWith("Added #3.", result);
    }

    [Fact]
    public async Task TaskAdd_BlockedByAtPosition_ReferencesATaskOfTheSameCall()
    {
        TaskListToolCollection collection = Create();

        string result = await collection.TaskAdd([new("Build"), new("Test", BlockedBy: ["@1"])]);

        Assert.StartsWith("Added #1,#2.", result);
        Assert.Contains("#2 [pending] Test (blocked by #1)", (await collection.GetContextAsync(default))!);
    }

    [Fact]
    public async Task TaskAdd_BlockedByExistingId_Works()
    {
        TaskListToolCollection collection = await CreateWithAsync("Build");

        string result = await collection.TaskAdd([new("Test", BlockedBy: ["1"])]);

        Assert.StartsWith("Added #2.", result);
        Assert.Contains("#2 [pending] Test (blocked by #1)", (await collection.GetContextAsync(default))!);
    }

    [Theory]
    [InlineData("9")]
    [InlineData("@3")]
    [InlineData("@0")]
    [InlineData("nonsense")]
    public async Task TaskAdd_BlockedByUnknownReference_ChangesNothing(string reference)
    {
        TaskListToolCollection collection = await CreateWithAsync("Build");

        string result = await collection.TaskAdd([new("Test", BlockedBy: [reference]), new("Ship")]);

        Assert.StartsWith("Error:", result);
        Assert.EndsWith("Nothing was changed.", result);
        Assert.DoesNotContain("Test", (await collection.GetContextAsync(default))!);
    }

    [Fact]
    public async Task TaskAdd_BlockedByItself_IsALoop()
    {
        TaskListToolCollection collection = Create();

        string result = await collection.TaskAdd([new("Only", BlockedBy: ["@1"])]);

        Assert.Contains("loop", result);
        Assert.Null(await collection.GetContextAsync(default));
    }

    [Fact]
    public async Task TaskAdd_BlankSubject_ChangesNothing()
    {
        TaskListToolCollection collection = Create();

        string result = await collection.TaskAdd([new("Fine"), new("  ")]);

        Assert.StartsWith("Error: Task 2 needs a subject", result);
        Assert.Null(await collection.GetContextAsync(default));
    }

    [Fact]
    public async Task TaskAdd_EmptyList_IsAnError()
    {
        Assert.StartsWith("Error:", await Create().TaskAdd([]));
    }

    // ---- TaskUpdate ----

    [Fact]
    public async Task TaskUpdate_StartAndComplete_ReportsTheNextTask()
    {
        TaskListToolCollection collection = await CreateWithAsync("A", "B");

        Assert.Equal("Updated #1 → in_progress.", await collection.TaskUpdate([Set(1, TaskItemStatus.InProgress)]));
        Assert.Equal("Updated #1 → completed. Next: #2.", await collection.TaskUpdate([Set(1, TaskItemStatus.Completed)]));
    }

    [Fact]
    public async Task TaskUpdate_CompletingTheLastTask_SaysAllDone()
    {
        TaskListToolCollection collection = await CreateWithAsync("A");

        string result = await collection.TaskUpdate([Set(1, TaskItemStatus.Completed)]);

        Assert.Equal("Updated #1 → completed. All tasks completed.", result);
    }

    [Fact]
    public async Task TaskUpdate_CompleteOneAndStartTheNextInOneCall_IsValidUnderSingleInProgress()
    {
        TaskListToolCollection collection = await CreateWithAsync("A", "B");
        await collection.TaskUpdate([Set(1, TaskItemStatus.InProgress)]);

        string result = await collection.TaskUpdate([Set(2, TaskItemStatus.InProgress), Set(1, TaskItemStatus.Completed)]);

        Assert.StartsWith("Updated", result);
    }

    [Fact]
    public async Task TaskUpdate_SecondInProgress_IsRejectedAndNothingChanges()
    {
        TaskListToolCollection collection = await CreateWithAsync("A", "B");
        await collection.TaskUpdate([Set(1, TaskItemStatus.InProgress)]);

        string result = await collection.TaskUpdate([Set(2, TaskItemStatus.InProgress)]);

        Assert.Contains("Only one task can be in_progress", result);
        Assert.Contains("#2 [pending]", (await collection.GetContextAsync(default))!);
    }

    [Fact]
    public async Task TaskUpdate_SecondInProgress_IsAllowedWhenTheOptionIsOff()
    {
        TaskListToolCollection collection = Create(new TaskListOptions { SingleInProgress = false });
        await collection.TaskAdd(Add("A", "B"));

        string result = await collection.TaskUpdate([Set(1, TaskItemStatus.InProgress), Set(2, TaskItemStatus.InProgress)]);

        Assert.StartsWith("Updated", result);
    }

    [Theory]
    [InlineData(TaskItemStatus.InProgress)]
    [InlineData(TaskItemStatus.Completed)]
    public async Task TaskUpdate_StartingOrCompletingATaskBehindAnOpenBlocker_IsRejected(TaskItemStatus status)
    {
        TaskListToolCollection collection = Create();
        await collection.TaskAdd([new("Build"), new("Test", BlockedBy: ["@1"])]);

        string result = await collection.TaskUpdate([Set(2, status)]);

        Assert.Contains("waits on open #1", result);
    }

    [Fact]
    public async Task TaskUpdate_CompletingABlockerAndItsDependentTogether_IsValid()
    {
        TaskListToolCollection collection = Create();
        await collection.TaskAdd([new("Build"), new("Test", BlockedBy: ["@1"])]);

        string result = await collection.TaskUpdate([Set(2, TaskItemStatus.Completed), Set(1, TaskItemStatus.Completed)]);

        Assert.Equal("Updated #2 → completed; #1 → completed. All tasks completed.", result);
    }

    [Fact]
    public async Task TaskUpdate_AllOrNothing_AnInvalidEntryUndoesTheValidOnes()
    {
        TaskListToolCollection collection = await CreateWithAsync("A", "B");

        string result = await collection.TaskUpdate([Set(1, TaskItemStatus.Completed), Set(9, TaskItemStatus.Completed)]);

        Assert.Equal("Error: There is no task #9. Nothing was changed.", result);
        Assert.Contains("#1 [pending]", (await collection.GetContextAsync(default))!);
    }

    [Fact]
    public async Task TaskUpdate_AddingABlockerThatClosesALoop_IsRejected()
    {
        TaskListToolCollection collection = Create();
        await collection.TaskAdd([new("A"), new("B", BlockedBy: ["@1"])]);

        string result = await collection.TaskUpdate([new(1, AddBlockedBy: [2])]);

        Assert.Contains("#1 blocked by #2 blocked by #1", result);
    }

    [Fact]
    public async Task TaskUpdate_BlockerThatIsAlreadyCompleted_IsAcceptedAndDoesNotBlock()
    {
        TaskListToolCollection collection = await CreateWithAsync("A", "B");
        await collection.TaskUpdate([Set(1, TaskItemStatus.Completed)]);

        string result = await collection.TaskUpdate([new(2, AddBlockedBy: [1])]);

        Assert.StartsWith("Updated #2 waits on #1", result);
        Assert.DoesNotContain("blocked by", (await collection.GetContextAsync(default))!);
    }

    [Fact]
    public async Task TaskUpdate_RemovingABlocker_LetsTheTaskStart()
    {
        TaskListToolCollection collection = Create();
        await collection.TaskAdd([new("A"), new("B", BlockedBy: ["@1"])]);

        await collection.TaskUpdate([new(2, RemoveBlockedBy: [1])]);

        Assert.StartsWith("Updated", await collection.TaskUpdate([Set(2, TaskItemStatus.InProgress)]));
    }

    [Fact]
    public async Task TaskUpdate_SelfBlockerUnknownBlockerDuplicateIdAndEmptyEntry_AreRejected()
    {
        TaskListToolCollection collection = await CreateWithAsync("A", "B");

        Assert.Contains("cannot wait on itself", await collection.TaskUpdate([new(1, AddBlockedBy: [1])]));
        Assert.Contains("no such task", await collection.TaskUpdate([new(1, AddBlockedBy: [7])]));
        Assert.Contains("appears twice", await collection.TaskUpdate([Set(1, TaskItemStatus.InProgress), new(1, Subject: "x")]));
        Assert.Contains("changes nothing", await collection.TaskUpdate([new(1)]));
        Assert.Contains("is blank", await collection.TaskUpdate([new(1, Subject: " ")]));
    }

    [Fact]
    public async Task TaskUpdate_SameStatus_IsAcceptedSilently()
    {
        TaskListToolCollection collection = await CreateWithAsync("A");

        Assert.Equal("Updated #1 unchanged. Next: #1.", await collection.TaskUpdate([Set(1, TaskItemStatus.Pending)]));
    }

    [Fact]
    public async Task TaskUpdate_RenameAndDescribe_AreShownByTaskGet()
    {
        TaskListToolCollection collection = await CreateWithAsync("A");

        await collection.TaskUpdate([new(1, Subject: "Alpha", Description: "More detail")]);

        Assert.Equal("Tasks (0/1 done):\n#1 [pending] Alpha\n  More detail", await collection.TaskGet());
    }

    // ---- TaskRemove ----

    [Fact]
    public async Task TaskRemove_RemovesTheTaskAndFreesItsDependents()
    {
        TaskListToolCollection collection = Create();
        await collection.TaskAdd([new("A"), new("B", BlockedBy: ["@1"])]);

        string result = await collection.TaskRemove([1]);

        Assert.Equal("Removed #1. Next: #2.", result);
        Assert.DoesNotContain("blocked by", (await collection.GetContextAsync(default))!);
    }

    [Fact]
    public async Task TaskRemove_IdsAreNeverReused()
    {
        TaskListToolCollection collection = await CreateWithAsync("A", "B");
        await collection.TaskRemove([2]);

        Assert.StartsWith("Added #3.", await collection.TaskAdd(Add("C")));
    }

    [Fact]
    public async Task TaskRemove_TheInProgressTask_IsAllowed()
    {
        TaskListToolCollection collection = await CreateWithAsync("A", "B");
        await collection.TaskUpdate([Set(1, TaskItemStatus.InProgress)]);

        Assert.StartsWith("Removed #1.", await collection.TaskRemove([1]));
    }

    [Fact]
    public async Task TaskRemove_UnknownId_ChangesNothing()
    {
        TaskListToolCollection collection = await CreateWithAsync("A");

        string result = await collection.TaskRemove([1, 5]);

        Assert.Equal("Error: There is no task #5. Nothing was changed.", result);
        Assert.Contains("#1", (await collection.GetContextAsync(default))!);
    }

    // ---- TaskGet ----

    [Fact]
    public async Task TaskGet_WithoutIds_ListsEverythingIncludingCompletedAndBlockers()
    {
        TaskListToolCollection collection = Create();
        await collection.TaskAdd([new("Build", "Compile it"), new("Test", BlockedBy: ["@1"])]);
        await collection.TaskUpdate([Set(1, TaskItemStatus.Completed)]);

        string result = await collection.TaskGet();

        Assert.Equal("Tasks (1/2 done):\n#1 [completed] Build\n  Compile it\n#2 [pending] Test\n  Blocked by: #1", result);
    }

    [Fact]
    public async Task TaskGet_WithIds_ReadsThoseAndNamesMissingOnes()
    {
        TaskListToolCollection collection = await CreateWithAsync("A", "B");

        Assert.Equal("#2 [pending] B\n#9 not found.", await collection.TaskGet([2, 9]));
    }

    [Fact]
    public async Task TaskGet_EmptyList_SaysSo()
    {
        Assert.Equal("No tasks.", await Create().TaskGet());
    }

    // ---- context ----

    [Fact]
    public async Task GetContextAsync_EmptyList_IsNull()
    {
        Assert.Null(await Create().GetContextAsync(default));
    }

    [Fact]
    public async Task GetContextAsync_OrdersInProgressThenStartableThenBlocked_AndCountsCompleted()
    {
        TaskListToolCollection collection = Create();
        await collection.TaskAdd(
        [
            new("Done"),
            new("Blocked", BlockedBy: ["@4"]),
            new("Startable"),
            new("Active")
        ]);
        await collection.TaskUpdate([Set(1, TaskItemStatus.Completed), Set(4, TaskItemStatus.InProgress)]);

        string? context = await collection.GetContextAsync(default);

        Assert.Equal(
            "## Task list (1/4 done)\n> #4 [in_progress] Active\n#3 [pending] Startable\n#2 [pending] Blocked (blocked by #4)\nCompleted: 1.",
            context);
    }

    [Fact]
    public async Task GetContextAsync_CapsOpenTasksAndCountsTheHiddenOnes()
    {
        TaskListToolCollection collection = Create(new TaskListOptions { MaxVisibleItems = 2 });
        await collection.TaskAdd(Add("A", "B", "C", "D"));

        string? context = await collection.GetContextAsync(default);

        Assert.Equal("## Task list (0/4 done)\n#1 [pending] A\n#2 [pending] B\n+2 more open (TaskGet lists all).", context);
    }

    [Fact]
    public async Task GetContextAsync_AllCompleted_IsOneShortLine()
    {
        TaskListToolCollection collection = await CreateWithAsync("A", "B");
        await collection.TaskUpdate([Set(1, TaskItemStatus.Completed), Set(2, TaskItemStatus.Completed)]);

        Assert.Equal("## Task list (2/2 done)\nAll tasks completed.", await collection.GetContextAsync(default));
    }

    [Fact]
    public async Task GetContextAsync_TruncatesLongSubjectsAndCollapsesLineBreaks()
    {
        TaskListToolCollection collection = Create(new TaskListOptions { MaxSubjectLength = 20 });
        await collection.TaskAdd(Add("A very long subject that goes on\nand on"));

        string? context = await collection.GetContextAsync(default);

        Assert.Contains("#1 [pending] A very long subje...", context);
        Assert.DoesNotContain("\nand on", context);
    }

    [Fact]
    public async Task GetContextAsync_InjectContextOff_IsNull()
    {
        TaskListToolCollection collection = Create(new TaskListOptions { InjectContext = false });
        await collection.TaskAdd(Add("A"));

        Assert.Null(await collection.GetContextAsync(default));
        Assert.Contains(collection.Instructions, text => text.Contains("TaskGet to read it"));
    }

    [Fact]
    public async Task GetContextAsync_CustomFormatter_ReplacesTheView()
    {
        TaskListToolCollection collection = Create(new TaskListOptions { ContextFormatter = snapshot => $"{snapshot.Items.Count} tasks" });
        await collection.TaskAdd(Add("A", "B"));

        Assert.Equal("2 tasks", await collection.GetContextAsync(default));
    }

    [Fact]
    public async Task GetContextAsync_FiftyTasks_StaysCompact()
    {
        TaskListToolCollection collection = Create();
        await collection.TaskAdd(Add([.. Enumerable.Range(1, 50).Select(i => $"Task number {i} with a typical short subject")]));

        string context = (await collection.GetContextAsync(default))!;

        // A character budget as a stand-in for tokens (roughly 4 characters each): 15 visible lines plus the header and footer.
        Assert.True(context.Length < 1000, $"The view of 50 tasks is {context.Length} characters.");
    }

    // ---- storage ----

    [Fact]
    public async Task Storage_IsReadOnceAndSavedAfterEveryChange()
    {
        InMemoryTaskListStorage storage = new();
        TaskListToolCollection collection = Create(storage: storage);

        await collection.TaskAdd(Add("A"));
        await collection.GetContextAsync(default);
        await collection.GetContextAsync(default);
        await collection.TaskGet();
        await collection.TaskUpdate([Set(1, TaskItemStatus.InProgress)]);
        await collection.TaskUpdate([Set(9, TaskItemStatus.InProgress)]);

        Assert.Equal(1, storage.LoadCount);
        Assert.Equal(2, storage.SaveCount);
    }

    [Fact]
    public async Task Storage_AStoredListIsPickedUpByANewCollection()
    {
        InMemoryTaskListStorage storage = new();
        await Create(storage: storage).TaskAdd(Add("A", "B"));

        TaskListToolCollection second = Create(storage: storage);

        Assert.Contains("#2 [pending] B", (await second.GetContextAsync(default))!);
        Assert.StartsWith("Added #3.", await second.TaskAdd(Add("C")));
    }

    [Fact]
    public async Task Storage_SaveFailure_ChangesNothingAndTellsTheModel()
    {
        FailingStorage storage = new();
        TaskListToolCollection collection = new(storage);

        string result = await collection.TaskAdd(Add("A"));

        Assert.StartsWith("Error: The task list could not be saved", result);
        Assert.Null(await collection.GetContextAsync(default));
    }

    [Fact]
    public async Task Storage_LoadFailure_ReturnsAnErrorFromTools()
    {
        FailingStorage storage = new() { FailLoad = true };
        TaskListToolCollection collection = new(storage);

        Assert.StartsWith("Error: The task list could not be read", await collection.TaskAdd(Add("A")));
        Assert.StartsWith("Error: The task list could not be read", await collection.TaskGet());
    }

    [Fact]
    public async Task Storage_ARepairableStoredList_IsCleanedUp()
    {
        InMemoryTaskListStorage storage = new();
        await storage.SaveAsync(new TaskListSnapshot
        {
            NextId = 1,
            Items =
            [
                new TaskItem { Id = 5, Subject = "A", BlockedBy = [5, 99, 6] },
                new TaskItem { Id = 5, Subject = "Duplicate" },
                new TaskItem { Id = 6, Subject = "B" }
            ]
        }, default);
        TaskListToolCollection collection = Create(storage: storage);

        Assert.Equal("## Task list (0/2 done)\n#6 [pending] B\n#5 [pending] A (blocked by #6)", await collection.GetContextAsync(default));
        Assert.StartsWith("Added #7.", await collection.TaskAdd(Add("C")));
    }

    // ---- concurrency ----

    [Fact]
    public async Task ParallelCalls_AreSerialized_AndEveryIdIsUnique()
    {
        TaskListToolCollection collection = Create();

        await Task.WhenAll(Enumerable.Range(0, 40).Select(i => Task.Run(() => collection.TaskAdd(Add($"Task {i}")))));

        string all = await collection.TaskGet();
        Assert.StartsWith("Tasks (0/40 done):", all);
        Assert.Equal(40, all.Split('\n').Count(line => line.StartsWith('#')));
    }

    // ---- wiring ----

    [Fact]
    public void Constructor_RejectsTooSmallLimitsAndNullStorage()
    {
        Assert.Throws<ArgumentNullException>(() => new TaskListToolCollection(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(new TaskListOptions { MaxVisibleItems = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(new TaskListOptions { MaxSubjectLength = 3 }));
    }

    [Fact]
    public void Tools_AreTaskAddTaskUpdateTaskRemoveAndTaskGet()
    {
        Assert.Equal(["TaskAdd", "TaskGet", "TaskRemove", "TaskUpdate"], Create().GetAIFunctions().Select(f => f.Name).Order());
    }

    [Fact]
    public async Task Tools_WorkThroughTheirAIFunctions_WithStatusesAsStrings()
    {
        TaskListToolCollection collection = Create();
        AIFunction add = collection.GetAIFunctions().Single(f => f.Name == "TaskAdd");
        AIFunction update = collection.GetAIFunctions().Single(f => f.Name == "TaskUpdate");

        object? added = await add.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["tasks"] = System.Text.Json.JsonDocument.Parse("""[{"subject":"A"},{"subject":"B","blockedBy":["@1"]}]""").RootElement
        }));
        object? updated = await update.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["updates"] = System.Text.Json.JsonDocument.Parse("""[{"id":1,"status":"in_progress"}]""").RootElement
        }));

        Assert.Contains("Added #1,#2.", added!.ToString());
        Assert.Contains("#1 → in_progress", updated!.ToString());
    }

    [Fact]
    public void StatusSchema_OffersTheThreeStatuses()
    {
        AIFunction update = Create().GetAIFunctions().Single(f => f.Name == "TaskUpdate");

        string schema = update.JsonSchema.GetRawText();

        Assert.Contains("in_progress", schema);
        Assert.Contains("completed", schema);
        Assert.Contains("pending", schema);
    }

    private sealed class FailingStorage : ITaskListStorage
    {
        public bool FailLoad { get; init; }

        public ValueTask<TaskListSnapshot?> LoadAsync(CancellationToken cancellationToken) =>
            FailLoad ? throw new IOException("disk gone") : ValueTask.FromResult<TaskListSnapshot?>(null);

        public ValueTask SaveAsync(TaskListSnapshot snapshot, CancellationToken cancellationToken) => throw new IOException("disk full");
    }
}
