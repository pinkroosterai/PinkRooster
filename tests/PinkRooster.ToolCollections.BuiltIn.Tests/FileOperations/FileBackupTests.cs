using Microsoft.Extensions.AI;
using PinkRooster.ToolCollections.BuiltIn.FileOperations;
using PinkRooster.ToolCollections.BuiltIn.Shared;
using PinkRooster.ToolCollections.BuiltIn.Tests.TestSupport;

namespace PinkRooster.ToolCollections.BuiltIn.Tests.FileOperations;

/// <summary>The read-only collection, and the backups a host can ask the write tools to keep.</summary>
public sealed class FileBackupTests : IDisposable
{
    private readonly TempDirectory temp = new("filebackup-test-");

    private string root => temp.Path;

    private string BackupDirectory => Path.Combine(root, ".pinkrooster", "backups");

    public void Dispose() => temp.Dispose();

    private FileOperationsToolCollection Collection(bool keepBackups = true) => new(new Workspace(root), keepBackups: keepBackups);

    private string Write(string relativePath, string content)
    {
        string path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void ReadOnlyCollection_HasOnlyTheToolsThatReadAndNoApprovals()
    {
        IReadOnlyList<AIFunction> tools = new FileReadToolCollection(new Workspace(root)).GetAIFunctions();

        Assert.Equal(["FindFiles", "ListDirectory", "ReadFile", "SearchFiles"], tools.Select(tool => tool.Name).Order());
        Assert.DoesNotContain(tools, tool => tool is ApprovalRequiredAIFunction);
    }

    [Fact]
    public async Task ReadOnlyCollection_ReadsAndSearchesLikeTheFullOne()
    {
        Write("a.txt", "needle\n");
        var readOnly = new FileReadToolCollection(new Workspace(root));

        Assert.Equal("Lines 1-1 (of 1, end of file):\n1: needle", await readOnly.ReadFile("a.txt"));
        Assert.Equal("a.txt", await readOnly.SearchFiles("needle"));
    }

    [Fact]
    public async Task EditFile_WithBackups_KeepsThePreviousContentAndNamesIt()
    {
        string path = Write("src/a.txt", "one\ntwo\n");

        var result = await Collection().EditFile("src/a.txt", "two", "2");

        string backup = Assert.Single(Directory.GetFiles(BackupDirectory));
        Assert.EndsWith($"-src__a.txt.bak", backup);
        Assert.Equal("one\ntwo\n", File.ReadAllText(backup));
        Assert.Equal("one\n2\n", File.ReadAllText(path));
        Assert.EndsWith($" Previous content kept in .pinkrooster/backups/{Path.GetFileName(backup)}.", result);
    }

    [Fact]
    public async Task WriteFile_WithBackups_KeepsThePreviousContent()
    {
        Write("a.txt", "old");

        var result = await Collection().WriteFile("a.txt", "new");

        Assert.Equal("old", File.ReadAllText(Assert.Single(Directory.GetFiles(BackupDirectory))));
        Assert.Contains("Previous content kept in .pinkrooster/backups/", result);
    }

    [Fact]
    public async Task DeleteFile_WithBackups_KeepsTheContentOfTheDeletedFile()
    {
        string path = Write("a.txt", "keep me");

        var result = await Collection().DeleteFile("a.txt");

        Assert.False(File.Exists(path));
        Assert.Equal("keep me", File.ReadAllText(Assert.Single(Directory.GetFiles(BackupDirectory))));
        Assert.Contains("Previous content kept in", result);
    }

    [Fact]
    public async Task WithoutBackups_NothingIsKeptAndTheReplyHasNoNote()
    {
        Write("a.txt", "old");

        var result = await Collection(keepBackups: false).WriteFile("a.txt", "new");

        Assert.Equal("File overwritten successfully: 'a.txt' (3 bytes).", result);
        Assert.False(Directory.Exists(Path.Combine(root, ".pinkrooster")));
    }

    [Fact]
    public async Task ABackupThatCannotBeMade_StopsTheChange()
    {
        string path = Write("a.txt", "old");
        File.WriteAllText(Path.Combine(root, ".pinkrooster"), "not a folder");

        var edit = await Collection().EditFile("a.txt", "old", "new");
        var write = await Collection().WriteFile("a.txt", "new");
        var delete = await Collection().DeleteFile("a.txt");

        Assert.All([edit, write, delete], reply => Assert.StartsWith("Error: Could not keep a backup, so 'a.txt' was not changed:", reply));
        Assert.Equal("old", File.ReadAllText(path));
    }

    [Fact]
    public async Task BackupsFolderLinkedOutsideTheWorkspace_IsNotWrittenTo()
    {
        using var outside = new TempDirectory("filebackup-outside-");
        string path = Write("a.txt", "old");
        SymbolicLinks.ToDirectory(Path.Combine(root, ".pinkrooster"), outside.Path);

        var result = await Collection().WriteFile("a.txt", "new");

        Assert.StartsWith("Error: Could not keep a backup", result);
        Assert.Empty(Directory.GetFileSystemEntries(outside.Path));
        Assert.Equal("old", File.ReadAllText(path));
    }

    [Fact]
    public async Task DeletingALink_KeepsNoBackupOfItsTarget()
    {
        string target = Write("target.txt", "data");
        SymbolicLinks.ToFile(Path.Combine(root, "link.txt"), target);

        await Collection().DeleteFile("link.txt");

        Assert.True(File.Exists(target));
        Assert.False(Directory.Exists(BackupDirectory));
    }

    [Fact]
    public async Task OldAndSurplusBackups_AreRemovedWhenANewOneIsMade()
    {
        Directory.CreateDirectory(BackupDirectory);
        for (int i = 0; i < 105; i++)
        {
            string old = Path.Combine(BackupDirectory, $"x-{i:D3}.bak");
            File.WriteAllText(old, "x");
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddMinutes(-i - 1));
        }

        string stale = Path.Combine(BackupDirectory, "stale.bak");
        File.WriteAllText(stale, "x");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-9));
        Write("a.txt", "old");

        await Collection().WriteFile("a.txt", "new");

        Assert.Equal(100, Directory.GetFiles(BackupDirectory, "*.bak").Length);
        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(Path.Combine(BackupDirectory, "x-000.bak")));
    }

    [Fact]
    public void Instructions_SayWhereBackupsGo_OnlyWhenOn()
    {
        Assert.Contains(Collection().Instructions, i => i.Contains(".pinkrooster/backups"));
        Assert.DoesNotContain(Collection(keepBackups: false).Instructions, i => i.Contains("backups"));
    }
}
