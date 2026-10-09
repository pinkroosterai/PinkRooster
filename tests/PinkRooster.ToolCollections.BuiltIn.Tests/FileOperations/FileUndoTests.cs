using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents.Tests.TestSupport;
using PinkRooster.ToolCollections.BuiltIn.FileOperations;
using PinkRooster.ToolCollections.BuiltIn.Tests.TestSupport;

namespace PinkRooster.ToolCollections.BuiltIn.Tests.FileOperations;

/// <summary>Undo of the file tools' changes, per user message and per session, on a MAF agent with a scripted model.</summary>
public sealed class FileUndoTests : IDisposable
{
    private readonly TempDirectory temp = new("fileundo-test-");

    private string root => temp.Path;

    public void Dispose() => temp.Dispose();

    private FileOperationsToolCollection Collection(bool keepBackups = true) => new(new Workspace(root), keepBackups: keepBackups);

    private string PathOf(string relativePath) => Path.Combine(root, relativePath);

    private string Write(string relativePath, string content)
    {
        string path = PathOf(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static ChatMessage Call(string name, params (string Name, object? Value)[] arguments) =>
        new(ChatRole.Assistant, [new FunctionCallContent(Guid.NewGuid().ToString("N"), name, arguments.ToDictionary(argument => argument.Name, argument => argument.Value))]);

    private static ChatMessage Edit(string path, string oldText, string newText) => Call("EditFile", ("path", path), ("oldText", oldText), ("newText", newText));

    private static ChatMessage Done() => new(ChatRole.Assistant, "done");

    private static AIAgent Agent(FileOperationsToolCollection files, params ChatMessage[] script) =>
        new ChatClientAgent(new ScriptedChatClient(script), tools: [.. files.GetAIFunctions()]);

    // One user message: the host marks the undo unit, sends the prompt and approves whatever asks, as an approval loop does.
    private static async Task SendAsync(AIAgent agent, FileOperationsToolCollection files, AgentSession session)
    {
        files.BeginUndoUnit(session);
        AgentResponse response = await agent.RunAsync("go", session, cancellationToken: TestContext.Current.CancellationToken);
        while (response.Messages.SelectMany(message => message.Contents).OfType<ToolApprovalRequestContent>().ToList() is { Count: > 0 } requests)
        {
            ChatMessage answers = new(ChatRole.User, [.. requests.Select(request => (AIContent)request.CreateResponse(true))]);
            response = await agent.RunAsync(answers, session, cancellationToken: TestContext.Current.CancellationToken);
        }
        Assert.Equal("done", response.Text);
    }

    [Fact]
    public async Task AMessageThatEditsOneFile_CreatesOne_AndMovesOne_IsFullyTakenBack()
    {
        string edited = Write("src/a.txt", "one\ntwo\n");
        string moved = Write("src/old.txt", "moved content\n");
        FileOperationsToolCollection files = Collection();
        AIAgent agent = Agent(files,
            Edit("src/a.txt", "two", "2"),
            Call("CreateFile", ("path", "src/new/b.txt"), ("content", "created\n")),
            Call("MoveFile", ("sourcePath", "src/old.txt"), ("destinationPath", "lib/renamed.txt")),
            Done());
        AgentSession session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
        await SendAsync(agent, files, session);
        Assert.Equal("one\n2\n", File.ReadAllText(edited));
        Assert.True(File.Exists(PathOf("src/new/b.txt")));
        Assert.False(File.Exists(moved));

        FileUndoResult result = await files.UndoAsync(session, TestContext.Current.CancellationToken);

        Assert.Equal("one\ntwo\n", File.ReadAllText(edited));
        Assert.False(File.Exists(PathOf("src/new/b.txt")));
        Assert.Equal("moved content\n", File.ReadAllText(moved));
        Assert.False(File.Exists(PathOf("lib/renamed.txt")));
        Assert.Empty(result.Skipped);
        Assert.Equal(
            [("src/a.txt", "its previous content was put back"), ("src/new/b.txt", "removed; it had been created"), ("src/old.txt", "moved back from 'lib/renamed.txt'")],
            result.Restored.Select(entry => (entry.Path, entry.Detail)));
    }

    [Fact]
    public async Task AFileWithWindowsLineEndingsAndAByteOrderMark_ComesBackByteForByte()
    {
        byte[] original = [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("first\r\nsecond\r\nthird\r\n")];
        File.WriteAllBytes(PathOf("a.txt"), original);
        File.WriteAllBytes(PathOf("b.txt"), original);
        FileOperationsToolCollection files = Collection();
        AIAgent agent = Agent(files, Edit("a.txt", "second", "2nd\nextra"), Call("WriteFile", ("path", "b.txt"), ("content", "all new\n")), Done());
        AgentSession session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
        await SendAsync(agent, files, session);
        Assert.NotEqual(original, File.ReadAllBytes(PathOf("a.txt")));
        Assert.NotEqual(original, File.ReadAllBytes(PathOf("b.txt")));

        FileUndoResult result = await files.UndoAsync(session, TestContext.Current.CancellationToken);

        Assert.Equal(original, File.ReadAllBytes(PathOf("a.txt")));
        Assert.Equal(original, File.ReadAllBytes(PathOf("b.txt")));
        Assert.Equal(["a.txt", "b.txt"], result.Restored.Select(entry => entry.Path));
    }

    [Fact]
    public async Task TwoSessionsOnOneCollection_DoNotUndoEachOthersChanges()
    {
        Write("a.txt", "a\n");
        Write("b.txt", "b\n");
        FileOperationsToolCollection files = Collection();
        AIAgent agent = Agent(files, Edit("a.txt", "a", "first session"), Done(), Edit("b.txt", "b", "second session"), Done());
        AgentSession first = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
        AgentSession second = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
        await SendAsync(agent, files, first);
        await SendAsync(agent, files, second);

        FileUndoResult result = await files.UndoAsync(first, TestContext.Current.CancellationToken);

        Assert.Equal(["a.txt"], result.Restored.Select(entry => entry.Path));
        Assert.Equal("a\n", File.ReadAllText(PathOf("a.txt")));
        Assert.Equal("second session\n", File.ReadAllText(PathOf("b.txt")));
        Assert.True((await files.UndoAsync(first, TestContext.Current.CancellationToken)).NothingToUndo);

        await files.UndoAsync(second, TestContext.Current.CancellationToken);
        Assert.Equal("b\n", File.ReadAllText(PathOf("b.txt")));
    }

    [Fact]
    public async Task ASecondUndo_TakesBackTheMessageBefore_AndWithNothingLeftItSaysSoAndChangesNothing()
    {
        Write("a.txt", "zero\n");
        FileOperationsToolCollection files = Collection();
        // The second message changes no file, so the first undo skips it and takes back the third.
        AIAgent agent = Agent(files, Edit("a.txt", "zero", "one"), Done(), Done(), Edit("a.txt", "one", "two"), Edit("a.txt", "two", "three"), Done());
        AgentSession session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
        await SendAsync(agent, files, session);
        await SendAsync(agent, files, session);
        await SendAsync(agent, files, session);
        Assert.Equal("three\n", File.ReadAllText(PathOf("a.txt")));

        FileUndoResult first = await files.UndoAsync(session, TestContext.Current.CancellationToken);
        Assert.Equal("one\n", File.ReadAllText(PathOf("a.txt")));
        Assert.Equal("a.txt", Assert.Single(first.Restored).Path);

        await files.UndoAsync(session, TestContext.Current.CancellationToken);
        Assert.Equal("zero\n", File.ReadAllText(PathOf("a.txt")));

        FileUndoResult nothing = await files.UndoAsync(session, TestContext.Current.CancellationToken);
        Assert.True(nothing.NothingToUndo);
        Assert.Contains("nothing to undo", nothing.ToString());
        Assert.Equal("zero\n", File.ReadAllText(PathOf("a.txt")));
    }

    [Fact]
    public async Task AFileSomeoneElseChangedSince_IsSkippedAndNamed_AndTheOthersAreRestored()
    {
        Write("mine.txt", "before\n");
        Write("theirs.txt", "before\n");
        FileOperationsToolCollection files = Collection();
        AIAgent agent = Agent(files, Edit("mine.txt", "before", "after"), Edit("theirs.txt", "before", "after"), Call("CreateFile", ("path", "made.txt"), ("content", "made\n")), Done());
        AgentSession session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
        await SendAsync(agent, files, session);
        File.WriteAllText(PathOf("theirs.txt"), "the user's own edit\n");
        File.AppendAllText(PathOf("made.txt"), "the user's own line\n");

        FileUndoResult result = await files.UndoAsync(session, TestContext.Current.CancellationToken);

        Assert.Equal("before\n", File.ReadAllText(PathOf("mine.txt")));
        Assert.Equal("the user's own edit\n", File.ReadAllText(PathOf("theirs.txt")));
        Assert.Equal("made\nthe user's own line\n", File.ReadAllText(PathOf("made.txt")));
        Assert.Equal(["mine.txt"], result.Restored.Select(entry => entry.Path));
        Assert.Equal([("made.txt", "it was changed by someone else since"), ("theirs.txt", "it was changed by someone else since")], result.Skipped.Select(entry => (entry.Path, entry.Detail)));
    }

    [Fact]
    public async Task AFileWhoseBackupWasPruned_IsNamedAndLeft_AndTheRestIsRestored()
    {
        Write("kept.txt", "before\n");
        Write("pruned.txt", "before\n");
        FileOperationsToolCollection files = Collection();
        AIAgent agent = Agent(files, Edit("kept.txt", "before", "after"), Edit("pruned.txt", "before", "after"), Done());
        AgentSession session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
        await SendAsync(agent, files, session);
        File.Delete(Assert.Single(Directory.GetFiles(Path.Combine(root, ".pinkrooster", "backups"), "*pruned.txt.bak")));

        FileUndoResult result = await files.UndoAsync(session, TestContext.Current.CancellationToken);

        Assert.Equal("before\n", File.ReadAllText(PathOf("kept.txt")));
        Assert.Equal("after\n", File.ReadAllText(PathOf("pruned.txt")));
        Assert.Equal(["kept.txt"], result.Restored.Select(entry => entry.Path));
        FileUndoEntry skipped = Assert.Single(result.Skipped);
        Assert.Equal("pruned.txt", skipped.Path);
        Assert.Contains("copy of its previous content is gone", skipped.Detail);
    }

    [Fact]
    public async Task ADeletedFile_ComesBack_AndAFileChangedTwiceInOneMessage_GoesBackToWhereItStarted()
    {
        Write("gone.txt", "do not lose me\n");
        Write("twice.txt", "start\n");
        FileOperationsToolCollection files = Collection();
        AIAgent agent = Agent(files,
            Call("DeleteFile", ("path", "gone.txt")),
            Edit("twice.txt", "start", "middle"),
            Call("WriteFile", ("path", "twice.txt"), ("content", "end\n")),
            Done());
        AgentSession session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
        await SendAsync(agent, files, session);
        Assert.False(File.Exists(PathOf("gone.txt")));

        FileUndoResult result = await files.UndoAsync(session, TestContext.Current.CancellationToken);

        Assert.Equal("do not lose me\n", File.ReadAllText(PathOf("gone.txt")));
        Assert.Equal("start\n", File.ReadAllText(PathOf("twice.txt")));
        Assert.Equal([("gone.txt", "put back; it had been deleted"), ("twice.txt", "its previous content was put back")], result.Restored.Select(entry => (entry.Path, entry.Detail)));
    }

    [Fact]
    public async Task ACreatedFileThatWasThenMoved_IsRemoved()
    {
        FileOperationsToolCollection files = Collection();
        AIAgent agent = Agent(files,
            Call("CreateFile", ("path", "draft.txt"), ("content", "draft\n")),
            Call("MoveFile", ("sourcePath", "draft.txt"), ("destinationPath", "final.txt")),
            Done());
        AgentSession session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
        await SendAsync(agent, files, session);

        FileUndoResult result = await files.UndoAsync(session, TestContext.Current.CancellationToken);

        Assert.False(File.Exists(PathOf("draft.txt")));
        Assert.False(File.Exists(PathOf("final.txt")));
        Assert.Equal("draft.txt", Assert.Single(result.Restored).Path);
    }

    [Fact]
    public async Task TheRecord_IsSavedAndRestoredWithTheSession_SoUndoWorksAfterARestart()
    {
        Write("a.txt", "before\n");
        FileOperationsToolCollection files = Collection();
        AIAgent agent = Agent(files, Edit("a.txt", "before", "after"), Done());
        AgentSession session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
        await SendAsync(agent, files, session);
        JsonElement saved = await agent.SerializeSessionAsync(session, cancellationToken: TestContext.Current.CancellationToken);

        // Another process: a new collection and a new agent on the same workspace, and the session read back.
        FileOperationsToolCollection later = Collection();
        AgentSession restored = await Agent(later, Done()).DeserializeSessionAsync(saved, cancellationToken: TestContext.Current.CancellationToken);
        FileUndoResult result = await later.UndoAsync(restored, TestContext.Current.CancellationToken);

        Assert.Equal("before\n", File.ReadAllText(PathOf("a.txt")));
        Assert.Equal("a.txt", Assert.Single(result.Restored).Path);
    }

    [Fact]
    public async Task WithoutBeginUndoUnit_EveryChangeOfTheSessionIsOneUnit()
    {
        Write("a.txt", "zero\n");
        FileOperationsToolCollection files = Collection();
        AIAgent agent = Agent(files, Edit("a.txt", "zero", "one"), Done(), Edit("a.txt", "one", "two"), Done());
        AgentSession session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
        await agent.RunAsync("go", session, cancellationToken: TestContext.Current.CancellationToken);
        await agent.RunAsync("go", session, cancellationToken: TestContext.Current.CancellationToken);

        await files.UndoAsync(session, TestContext.Current.CancellationToken);

        Assert.Equal("zero\n", File.ReadAllText(PathOf("a.txt")));
    }

    [Fact]
    public async Task WithoutBackups_UndoThrowsNamingTheFix_AndNothingIsRecorded()
    {
        Write("a.txt", "before\n");
        FileOperationsToolCollection files = Collection(keepBackups: false);
        AIAgent agent = Agent(files, Edit("a.txt", "before", "after"), Done());
        AgentSession session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
        await SendAsync(agent, files, session);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => files.UndoAsync(session, TestContext.Current.CancellationToken));

        Assert.Contains("KeepBackups()", error.Message);
        Assert.Equal("after\n", File.ReadAllText(PathOf("a.txt")));
        Assert.DoesNotContain("FileChangeLog", (await agent.SerializeSessionAsync(session, cancellationToken: TestContext.Current.CancellationToken)).GetRawText());
    }

    [Fact]
    public void TheResultsText_NamesWhatWasRestoredAndWhatWasLeft()
    {
        FileUndoResult result = new([new FileUndoEntry("src/a.cs", "its previous content was put back")], [new FileUndoEntry("src/b.cs", "it was changed by someone else since")]);

        Assert.Equal(
            "Undo took back the file changes made for one user message. These files are now as listed, whatever the conversation above says.\n" +
            "Restored to what they were before that message:\n- src/a.cs: its previous content was put back\n" +
            "Left as they are:\n- src/b.cs: it was changed by someone else since",
            result.ToString());
    }
}
