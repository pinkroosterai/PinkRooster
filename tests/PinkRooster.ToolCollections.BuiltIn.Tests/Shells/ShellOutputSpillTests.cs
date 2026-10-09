using PinkRooster.ToolCollections.BuiltIn.FileOperations;
using PinkRooster.ToolCollections.BuiltIn.Shared;
using PinkRooster.ToolCollections.BuiltIn.Shells;
using PinkRooster.ToolCollections.BuiltIn.Tests.TestSupport;

namespace PinkRooster.ToolCollections.BuiltIn.Tests.Shells;

/// <summary>Output too long for a reply is saved under the workspace, where the file tools can read it.</summary>
public sealed class ShellOutputSpillTests : IDisposable
{
    private readonly TempDirectory temp = new("shell-spill-");

    private string root => temp.Path;

    private string OutputDirectory => Path.Combine(root, ".pinkrooster", "output");

    public void Dispose() => temp.Dispose();

    private ShellToolCollection Shell(int maxOutputCharacters = 1500, bool separateStreams = false) =>
        new(new Workspace(root), TimeSpan.FromSeconds(20), maxOutputCharacters, separateStreams: separateStreams);

    [Fact]
    public async Task OutputOverTheBudget_IsSavedInFullAndTheReplyNamesTheFile()
    {
        string result = await Shell().RunShell(ShellCommands.NumberedLines(2000));

        string file = Assert.Single(Directory.GetFiles(OutputDirectory, "shell-*.txt"));
        string relative = $".pinkrooster/output/{Path.GetFileName(file)}";
        Assert.Contains($"Output: 20000 characters; the reply is shortened, and all of it is saved in {relative} (read it with ReadFile, or search it with SearchFiles).", result);
        Assert.Contains(" characters omitted ...]", result);
        Assert.True(result.Length <= 1500, $"{result.Length} characters.");
        string[] saved = File.ReadAllLines(file);
        Assert.Equal(2000, saved.Length);
        Assert.Equal("line00001", saved[0]);
        Assert.Equal("line02000", saved[^1]);
    }

    [Fact]
    public async Task SavedOutput_CanBeReadBackWithTheFileTools()
    {
        var workspace = new Workspace(root);
        var shell = new ShellToolCollection(workspace, TimeSpan.FromSeconds(20), 1500);
        var files = new FileOperationsToolCollection(workspace);

        string result = await shell.RunShell(ShellCommands.NumberedLines(2000));
        string relative = System.Text.RegularExpressions.Regex.Match(result, @"saved in (\S+) ").Groups[1].Value;

        string page = await files.ReadFile(relative, startLine: 1500, endLine: 1502);
        Assert.Equal("Lines 1500-1502 (more lines follow; continue with startLine=1503):\n1500: line01500\n1501: line01501\n1502: line01502", page);
        Assert.Equal(relative, await files.SearchFiles("line01999", path: ".pinkrooster/output"));
    }

    [Fact]
    public async Task TheOutputFolder_IgnoresItselfInVersionControl()
    {
        await Shell().RunShell(ShellCommands.NumberedLines(2000));

        Assert.Equal("*\n", File.ReadAllText(Path.Combine(root, ".pinkrooster", ".gitignore")));
    }

    [Fact]
    public async Task OutputThatFits_LeavesNoFileBehind()
    {
        string result = await Shell().RunShell(ShellCommands.NumberedLines(20));

        Assert.DoesNotContain("saved in", result);
        Assert.Empty(Directory.Exists(OutputDirectory) ? Directory.GetFiles(OutputDirectory) : []);
    }

    [Fact]
    public async Task OutputJustUnderTheBudget_LeavesNoFileBehind()
    {
        // 130 lines are 1300 characters: past the point where saving starts, but short enough to be shown whole
        string result = await Shell().RunShell(ShellCommands.NumberedLines(130));

        Assert.DoesNotContain("saved in", result);
        Assert.DoesNotContain("omitted", result);
        Assert.Empty(Directory.GetFiles(OutputDirectory));
    }

    [Fact]
    public async Task SeparateStreams_DoNotSaveAFile()
    {
        string result = await Shell(separateStreams: true).RunShell(ShellCommands.NumberedLines(2000));

        Assert.Contains(" characters omitted ...]", result);
        Assert.False(Directory.Exists(Path.Combine(root, ".pinkrooster")));
    }

    [Fact]
    public async Task WithAFilter_TheFileHoldsTheLinesThatMatched()
    {
        string result = await Shell().RunShell(ShellCommands.NumberedLines(2000), outputFilters: ["line01"]);

        string file = Assert.Single(Directory.GetFiles(OutputDirectory, "shell-*.txt"));
        Assert.Contains("kept by the filter", result);
        Assert.Equal(1000, File.ReadAllLines(file).Length);
        Assert.All(File.ReadAllLines(file), line => Assert.StartsWith("line01", line));
    }

    [Fact]
    public async Task OldAndSurplusFiles_AreRemovedWhenANewOneIsMade()
    {
        Directory.CreateDirectory(OutputDirectory);
        for (int i = 0; i < 25; i++)
        {
            string path = Path.Combine(OutputDirectory, $"shell-old-{i:D2}.txt");
            File.WriteAllText(path, "x");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-i - 1));
        }

        string stale = Path.Combine(OutputDirectory, "shell-stale.txt");
        File.WriteAllText(stale, "x");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-3));
        string other = Path.Combine(OutputDirectory, "notes.txt");
        File.WriteAllText(other, "mine");

        await Shell().RunShell(ShellCommands.NumberedLines(2000));

        Assert.Equal(20, Directory.GetFiles(OutputDirectory, "shell-*.txt").Length);
        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(Path.Combine(OutputDirectory, "shell-old-00.txt")));
        Assert.False(File.Exists(Path.Combine(OutputDirectory, "shell-old-24.txt")));
        Assert.True(File.Exists(other));
    }

    [Fact]
    public async Task ASavingFailure_IsReportedAndTheReplyIsStillCut()
    {
        File.WriteAllText(Path.Combine(root, ".pinkrooster"), "not a folder");

        string result = await Shell().RunShell(ShellCommands.NumberedLines(2000));

        Assert.Contains("Warning: could not save the full output:", result);
        Assert.Contains(" characters omitted ...]", result);
        Assert.DoesNotContain("saved in", result);
    }

    [Fact]
    public async Task AnOutputFolderLinkedOutsideTheWorkspace_IsNotWrittenTo()
    {
        using var outside = new TempDirectory("shell-spill-outside-");
        SymbolicLinks.ToDirectory(Path.Combine(root, ".pinkrooster"), outside.Path);

        string result = await Shell().RunShell(ShellCommands.NumberedLines(2000));

        Assert.Contains("Warning: could not save the full output:", result);
        Assert.Empty(Directory.GetFileSystemEntries(outside.Path));
    }
}
