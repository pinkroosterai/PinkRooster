using Microsoft.Extensions.AI;
using PinkRooster.ToolCollections.BuiltIn.FileOperations;
using PinkRooster.ToolCollections.BuiltIn.Shared;
using PinkRooster.ToolCollections.BuiltIn.Tests.TestSupport;

namespace PinkRooster.ToolCollections.BuiltIn.Tests.FileOperations;

/// <summary>FindFiles and SearchFiles, on a small tree.</summary>
public sealed class FileSearchToolsTests : IDisposable
{
    private readonly TempDirectory temp = new("filesearch-test-");

    private string root => temp.Path;

    public void Dispose() => temp.Dispose();

    private FileOperationsToolCollection Collection(int maxReadCharacters = FileOperationsToolCollection.DefaultMaxReadCharacters) =>
        new(new Workspace(root), maxReadCharacters);

    private string Write(string relativePath, string content, DateTime? modified = null)
    {
        string path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        if (modified is { } time)
        {
            File.SetLastWriteTimeUtc(path, time);
        }

        return path;
    }

    #region FindFiles

    [Fact]
    public async Task FindFiles_PatternWithoutSlash_MatchesNamesAtAnyDepthNewestFirst()
    {
        Write("a.cs", "", new DateTime(2026, 1, 1));
        Write("src/b.cs", "", new DateTime(2026, 3, 1));
        Write("src/deep/c.cs", "", new DateTime(2026, 2, 1));
        Write("readme.md", "", new DateTime(2026, 4, 1));

        var result = await Collection().FindFiles("*.cs");

        Assert.Equal("src/b.cs\nsrc/deep/c.cs\na.cs", result);
    }

    [Fact]
    public async Task FindFiles_PatternWithFolders_IsMatchedBelowTheStartingFolder()
    {
        Write("src/b.cs", "");
        Write("src/deep/c.cs", "");
        Write("docs/d.cs", "");

        var all = await Collection().FindFiles("src/**/*.cs");
        var below = await Collection().FindFiles("deep/*.cs", path: "src");

        Assert.Equal(["src/b.cs", "src/deep/c.cs"], all.Split('\n').Order());
        Assert.Equal("src/deep/c.cs", below);
    }

    [Fact]
    public async Task FindFiles_SkipsTheWorkspacesSkippedFolders()
    {
        Write("keep.cs", "");
        Write("node_modules/pkg/x.cs", "");
        Write("obj/y.cs", "");
        Write(".git/z.cs", "");
        Write("sub/bin/w.cs", "");

        Assert.Equal("keep.cs", await Collection().FindFiles("*.cs"));
    }

    [Fact]
    public async Task FindFiles_StartingInASkippedFolder_StillSearchesIt()
    {
        Write("obj/y.cs", "");

        Assert.Equal("obj/y.cs", await Collection().FindFiles("*.cs", path: "obj"));
    }

    [Fact]
    public async Task FindFiles_MoreThanTheCap_ListsTheNewestHundredAndCountsTheRest()
    {
        for (int i = 0; i < 105; i++)
        {
            Write($"f{i:D3}.txt", "", new DateTime(2026, 1, 1).AddMinutes(i));
        }

        var result = await Collection().FindFiles("*.txt");

        string[] lines = result.Split('\n');
        Assert.Equal(101, lines.Length);
        Assert.Equal("f104.txt", lines[0]);
        Assert.Equal("f005.txt", lines[99]);
        Assert.Equal("[... 5 more files match; narrow the pattern or the path]", lines[100]);
    }

    [Fact]
    public async Task FindFiles_NothingMatches_SaysSo()
    {
        Write("a.cs", "");

        Assert.Equal("No files match '*.zzz'.", await Collection().FindFiles("*.zzz"));
    }

    [Fact]
    public async Task FindFiles_BlankPattern_ReturnsErrorWithAnExample()
    {
        var result = await Collection().FindFiles(" ");

        Assert.StartsWith("Error: pattern cannot be empty.", result);
    }

    [Fact]
    public async Task FindFiles_PathOutsideTheBase_IsRefused()
    {
        Assert.Contains("must stay inside the base directory", await Collection().FindFiles("*.cs", path: "../.."));
    }

    [Fact]
    public async Task FindFiles_PathThatIsAFile_ReturnsErrorNamingTheTools()
    {
        Write("a.cs", "");

        var result = await Collection().FindFiles("*.cs", path: "a.cs");

        Assert.StartsWith("Error: 'a.cs' is a file, not a directory.", result);
    }

    [Fact]
    public async Task FindFiles_LinkedFolderLeadingOutside_IsNotFollowed()
    {
        using var outside = new TempDirectory("filesearch-outside-");
        File.WriteAllText(Path.Combine(outside.Path, "secret.cs"), "x");
        Write("mine.cs", "");
        SymbolicLinks.ToDirectory(Path.Combine(root, "exit"), outside.Path);

        Assert.Equal("mine.cs", await Collection().FindFiles("*.cs"));
    }

    #endregion

    #region SearchFiles

    private void Tree()
    {
        Write("a.cs", "class A\n{\n    void Run() { }\n}\n");
        Write("src/b.cs", "class B\n{\n    void Run() { }\n    void Run2() { }\n}\n");
        Write("src/c.txt", "run later\n");
    }

    [Fact]
    public async Task SearchFiles_FilesMode_ListsFilesThatMatch()
    {
        Tree();

        var result = await Collection().SearchFiles(@"void Run\(");

        Assert.Equal("a.cs\nsrc/b.cs", result);
    }

    [Fact]
    public async Task SearchFiles_LinesMode_GivesPathLineAndText()
    {
        Tree();

        var result = await Collection().SearchFiles(@"void Run", mode: SearchMode.Lines);

        Assert.Equal("a.cs:3: void Run() { }\nsrc/b.cs:3: void Run() { }\nsrc/b.cs:4: void Run2() { }", result);
    }

    [Fact]
    public async Task SearchFiles_CountsMode_GivesMatchesPerFileAndATotal()
    {
        Tree();

        var result = await Collection().SearchFiles(@"void Run", mode: SearchMode.Counts);

        Assert.Equal("a.cs: 1\nsrc/b.cs: 2\nTotal: 3 matching lines in 2 files", result);
    }

    [Fact]
    public async Task SearchFiles_Include_LimitsTheFilesSearched()
    {
        Tree();

        Assert.Equal("src/c.txt", await Collection().SearchFiles("run", include: "*.txt"));
    }

    [Fact]
    public async Task SearchFiles_IgnoreCase_MatchesRegardlessOfCase()
    {
        Tree();

        Assert.Equal("a.cs\nsrc/b.cs\nsrc/c.txt", await Collection().SearchFiles("RUN", ignoreCase: true));
        Assert.Equal("No matches for 'RUN'.", await Collection().SearchFiles("RUN"));
    }

    [Fact]
    public async Task SearchFiles_PathNamesAFile_SearchesOnlyThatFile()
    {
        Tree();

        var result = await Collection().SearchFiles("Run", path: "src/b.cs", mode: SearchMode.Counts);

        Assert.Equal("src/b.cs: 2\nTotal: 2 matching lines in 1 files", result);
    }

    [Fact]
    public async Task SearchFiles_PathNamesAFolder_SearchesBelowItAndShowsPathsFromTheBase()
    {
        Tree();

        Assert.Equal("src/b.cs", await Collection().SearchFiles("class", path: "src"));
    }

    [Fact]
    public async Task SearchFiles_InvalidRegex_ReturnsTheEnginesMessage()
    {
        var result = await Collection().SearchFiles("Run(");

        Assert.StartsWith("Error: pattern is not a valid regular expression:", result);
        Assert.Contains("Run(", result);
    }

    [Fact]
    public async Task SearchFiles_MissingPath_ReturnsErrorNamingTheTools()
    {
        var result = await Collection().SearchFiles("x", path: "nope");

        Assert.Equal("Error: Path not found: 'nope'. Use ListDirectory or FindFiles to see what exists.", result);
    }

    [Fact]
    public async Task SearchFiles_BinaryAndNonUtf8Files_AreCountedNotSearched()
    {
        Write("text.txt", "needle\n");
        File.WriteAllBytes(Path.Combine(root, "bin.dat"), [0x6E, 0x65, 0x65, 0x64, 0x6C, 0x65, 0x00]);
        File.WriteAllBytes(Path.Combine(root, "latin1.txt"), [0x6E, 0x65, 0x65, 0x64, 0x6C, 0x65, 0xE9]);

        var result = await Collection().SearchFiles("needle");

        Assert.Equal("text.txt\n[2 files not searched: binary, not UTF-8, unreadable or over 10 MB]", result);
    }

    [Fact]
    public async Task SearchFiles_MoreMatchesThanTheLimit_ReportsTheLimitAndSaysSo()
    {
        Write("many.txt", string.Join("\n", Enumerable.Range(1, 50).Select(i => $"hit {i}")));

        var result = await Collection().SearchFiles("hit", mode: SearchMode.Lines, limit: 3);

        Assert.Equal("many.txt:1: hit 1\nmany.txt:2: hit 2\nmany.txt:3: hit 3\n[... more matches not shown; narrow the search with path or include]", result);
    }

    [Fact]
    public async Task SearchFiles_ResultOverTheReadLimit_IsCutAtTheLimit()
    {
        for (int i = 0; i < 30; i++)
        {
            Write($"file_{i:D2}.txt", "hit");
        }

        var result = await Collection(maxReadCharacters: 60).SearchFiles("hit");

        Assert.Contains("[... more matches not shown", result);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(result.Split('\n')[..^1].Aggregate((a, b) => a + "\n" + b)) <= 60);
    }

    [Fact]
    public async Task SearchFiles_LongMatchingLine_IsShortened()
    {
        Write("long.txt", "hit " + new string('x', 500));

        var result = await Collection().SearchFiles("hit", mode: SearchMode.Lines);

        Assert.Equal("long.txt:1: hit " + new string('x', 196) + " [...]", result);
    }

    [Fact]
    public async Task SearchFiles_InvalidLimit_ReturnsError()
    {
        Assert.Equal("Error: limit must be at least 1, but got 0.", await Collection().SearchFiles("x", limit: 0));
    }

    [Fact]
    public async Task SearchFiles_PathOutsideTheBase_IsRefused()
    {
        Assert.Contains("must stay inside the base directory", await Collection().SearchFiles("x", path: "../.."));
    }

    [Fact]
    public async Task SearchFiles_LinkedFolderAndLinkedFile_AreNotFollowed()
    {
        using var outside = new TempDirectory("filesearch-outside-");
        File.WriteAllText(Path.Combine(outside.Path, "secret.txt"), "needle");
        Write("mine.txt", "needle");
        SymbolicLinks.ToDirectory(Path.Combine(root, "exit"), outside.Path);
        SymbolicLinks.ToFile(Path.Combine(root, "peek.txt"), Path.Combine(outside.Path, "secret.txt"));

        Assert.Equal("mine.txt", await Collection().SearchFiles("needle"));
    }

    [Fact]
    public async Task SearchFiles_PathThatIsALinkOutside_IsRefused()
    {
        using var outside = new TempDirectory("filesearch-outside-");
        File.WriteAllText(Path.Combine(outside.Path, "secret.txt"), "needle");
        SymbolicLinks.ToDirectory(Path.Combine(root, "exit"), outside.Path);

        Assert.Contains("traverses outside the base directory", await Collection().SearchFiles("needle", path: "exit"));
    }

    [Fact]
    public async Task SearchFiles_PatternThatBacktracksForever_StopsWithAnError()
    {
        Write("slow.txt", new string('a', 45) + "b");

        var result = await Collection().SearchFiles("^(a+)+$");

        Assert.StartsWith("Error: the search took longer than 2 s on one line.", result);
    }

    [Fact]
    public async Task SearchFiles_ModeGivenAsTextByTheModel_IsUnderstood()
    {
        Tree();
        AIFunction function = Collection().GetAIFunctions().Single(f => f.Name == "SearchFiles");

        object? result = await function.InvokeAsync(new AIFunctionArguments { ["pattern"] = "void Run", ["mode"] = "Counts" });

        Assert.Contains("Total: 3 matching lines in 2 files", result?.ToString());
    }

    #endregion
}
