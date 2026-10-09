using System.Text;
using Microsoft.Extensions.AI;
using PinkRooster.ToolCollections.BuiltIn.FileOperations;
using PinkRooster.ToolCollections.BuiltIn.Shared;
using PinkRooster.ToolCollections.BuiltIn.Tests.TestSupport;

namespace PinkRooster.ToolCollections.BuiltIn.Tests.FileOperations;

public sealed class FileOperationsToolCollectionTests : IDisposable
{
    private readonly TempDirectory temp = new("filetools-test-");

    private string tempRoot => temp.Path;

    public void Dispose() => temp.Dispose();

    private FileOperationsToolCollection CreateCollection(int maxReadCharacters = FileOperationsToolCollection.DefaultMaxReadCharacters) =>
        new(new Workspace(tempRoot), maxReadCharacters);

    [Fact]
    public void Constructor_SetsPropertiesAndAddsInstructions()
    {
        var collection = CreateCollection(1024);

        Assert.Equal(Path.GetFileName(tempRoot), Path.GetFileName(collection.BaseDirectory));
        Assert.Equal(1024, collection.MaxReadCharacters);

        var instructions = collection.Instructions;
        Assert.Contains(instructions, i => i.Contains(collection.BaseDirectory));
        Assert.Contains(instructions, i => i.Contains("1024 characters"));
    }

    [Fact]
    public void Workspace_NonExistentRoot_ThrowsArgumentExceptionNamingIt()
    {
        var missingDir = Path.Combine(tempRoot, "missing");
        var ex = Assert.Throws<ArgumentException>(() => new Workspace(missingDir));
        Assert.Contains(missingDir, ex.Message);
    }

    [Fact]
    public void Approvals_OnlyWriteFileAndDeleteFileRequireApproval()
    {
        var collection = CreateCollection();
        IReadOnlyList<AIFunction> functions = collection.GetAIFunctions();

        Assert.Equal(9, functions.Count);

        var writeFile = Assert.Single(functions, f => f.Name == "WriteFile");
        var deleteFile = Assert.Single(functions, f => f.Name == "DeleteFile");

        Assert.IsType<ApprovalRequiredAIFunction>(writeFile);
        Assert.IsType<ApprovalRequiredAIFunction>(deleteFile);

        var unapproved = new[] { "ListDirectory", "ReadFile", "CreateFile", "EditFile", "MoveFile" };
        foreach (var name in unapproved)
        {
            var fn = Assert.Single(functions, f => f.Name == name);
            Assert.IsNotType<ApprovalRequiredAIFunction>(fn);
        }
    }

    #region ListDirectory

    [Fact]
    public async Task ListDirectory_EmptyDirectory_ReturnsEmptyMessage()
    {
        var collection = CreateCollection();
        var result = await collection.ListDirectory();

        Assert.Equal("Directory is empty.", result);
    }

    [Fact]
    public async Task ListDirectory_WithFilesAndSubdirs_ListsDirectoriesFirstThenFilesWithSizes()
    {
        var collection = CreateCollection();
        Directory.CreateDirectory(Path.Combine(tempRoot, "docs"));
        File.WriteAllText(Path.Combine(tempRoot, "test.txt"), "hello");

        var result = await collection.ListDirectory();

        Assert.Equal("docs/\ntest.txt (5 bytes)", result);
    }

    [Fact]
    public async Task ListDirectory_OnFile_ReturnsErrorAdvisingReadFile()
    {
        var collection = CreateCollection();
        File.WriteAllText(Path.Combine(tempRoot, "test.txt"), "hello");

        var result = await collection.ListDirectory("test.txt");

        Assert.StartsWith("Error:", result);
        Assert.Contains("Use ReadFile", result);
    }

    [Fact]
    public async Task ListDirectory_OverReadLimit_ReturnsWhatFitsAndCountsTheRest()
    {
        var collection = CreateCollection(maxReadCharacters: 50);
        for (int i = 0; i < 10; i++)
        {
            File.WriteAllText(Path.Combine(tempRoot, $"file_{i:D2}.txt"), "content");
        }

        var result = await collection.ListDirectory();

        // "file_00.txt (7 bytes)" is 21 bytes, so two entries fit in 50
        Assert.StartsWith("file_00.txt (7 bytes)\nfile_01.txt (7 bytes)\n[... 8 more entries not shown;", result);
        Assert.Contains("read limit of 50 characters", result);
    }

    [Fact]
    public async Task ListDirectory_LargeFile_PrintsItsSizeInPlainDigits()
    {
        var collection = CreateCollection();
        File.WriteAllBytes(Path.Combine(tempRoot, "big.bin"), new byte[1234567]);

        var result = await collection.ListDirectory();

        Assert.Equal("big.bin (1234567 bytes)", result);
    }

    #endregion

    #region ReadFile

    [Fact]
    public async Task ReadFile_SmallFile_ReturnsNumberedLines()
    {
        var collection = CreateCollection();
        File.WriteAllText(Path.Combine(tempRoot, "sample.txt"), "line 1\nline 2");

        var result = await collection.ReadFile("sample.txt");

        Assert.Equal("Lines 1-2 (of 2, end of file):\n1: line 1\n2: line 2", result);
    }

    [Fact]
    public async Task ReadFile_FileEndingInALineBreak_CountsLinesAsALineRangeReadDoes()
    {
        var collection = CreateCollection();
        File.WriteAllText(Path.Combine(tempRoot, "sample.txt"), "line 1\r\nline 2\r\n");

        var whole = await collection.ReadFile("sample.txt");
        var pastTheEnd = await collection.ReadFile("sample.txt", startLine: 3);

        Assert.Equal("Lines 1-2 (of 2, end of file):\n1: line 1\n2: line 2", whole);
        Assert.StartsWith("Error: startLine (3) is beyond the end of file 'sample.txt' (file has 2 lines)", pastTheEnd);
    }

    [Fact]
    public async Task ReadFile_FileWithByteOrderMark_ReturnsTheTextWithoutIt()
    {
        var collection = CreateCollection();
        File.WriteAllText(Path.Combine(tempRoot, "bom.txt"), "first\nsecond", new UTF8Encoding(true));

        var result = await collection.ReadFile("bom.txt");

        Assert.Equal("Lines 1-2 (of 2, end of file):\n1: first\n2: second", result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadFile_TextThatIsNotUtf8_ReturnsError(bool withLineRange)
    {
        var collection = CreateCollection();
        // "café" in Latin-1: 0xE9 is not valid UTF-8
        File.WriteAllBytes(Path.Combine(tempRoot, "latin1.txt"), [0x63, 0x61, 0x66, 0xE9, 0x0A]);

        var result = await collection.ReadFile("latin1.txt", startLine: withLineRange ? 1 : null);

        Assert.Equal("Error: File 'latin1.txt' contains binary or non-UTF-8 content and cannot be read.", result);
    }

    [Fact]
    public async Task ReadFile_EmptyFile_ReturnsEmptyIndicator()
    {
        var collection = CreateCollection();
        File.WriteAllText(Path.Combine(tempRoot, "empty.txt"), "");

        var result = await collection.ReadFile("empty.txt");

        Assert.Equal("File is empty (0 lines).", result);
    }

    [Fact]
    public async Task ReadFile_LargeFileWithoutRange_ReturnsTheFirstPageAndHowToContinue()
    {
        var collection = CreateCollection(maxReadCharacters: 100);
        File.WriteAllLines(Path.Combine(tempRoot, "large.txt"), Enumerable.Range(1, 200).Select(i => $"row {i}"));

        var result = await collection.ReadFile("large.txt");

        Assert.StartsWith("Lines 1-", result);
        Assert.Matches(@"^Lines 1-(\d+) \(more lines follow; continue with startLine=(\d+)\):\n1: row 1\n2: row 2\n", result);
        var match = System.Text.RegularExpressions.Regex.Match(result, @"^Lines 1-(\d+) \(more lines follow; continue with startLine=(\d+)\):");
        int last = int.Parse(match.Groups[1].Value);
        Assert.Equal(last + 1, int.Parse(match.Groups[2].Value));
        Assert.Contains($"\n{last}: row {last}", result);
        Assert.True(Encoding.UTF8.GetByteCount(result[(result.IndexOf('\n') + 1)..]) <= 100);

        var next = await collection.ReadFile("large.txt", startLine: last + 1);
        Assert.StartsWith($"Lines {last + 1}-", next);
        Assert.Contains($"{last + 1}: row {last + 1}", next);
    }

    [Fact]
    public async Task ReadFile_OneMegabyteFileWithoutRange_ReturnsAPageOfTheDefaultLimit()
    {
        var collection = CreateCollection();
        File.WriteAllLines(Path.Combine(tempRoot, "big.txt"), Enumerable.Range(1, 40000).Select(i => $"line number {i} of a big file"));

        var result = await collection.ReadFile("big.txt");

        Assert.Contains("more lines follow; continue with startLine=", result);
        Assert.True(Encoding.UTF8.GetByteCount(result) < FileOperationsToolCollection.DefaultMaxReadCharacters + 100);
    }

    [Fact]
    public async Task ReadFile_LineRange_ReturnsRequestedLines()
    {
        var collection = CreateCollection();
        var lines = Enumerable.Range(1, 20).Select(i => $"Line {i}").ToArray();
        File.WriteAllLines(Path.Combine(tempRoot, "numbers.txt"), lines);

        var result = await collection.ReadFile("numbers.txt", startLine: 5, endLine: 8);

        Assert.Equal("Lines 5-8 (more lines follow; continue with startLine=9):\n5: Line 5\n6: Line 6\n7: Line 7\n8: Line 8", result);
    }

    [Fact]
    public async Task ReadFile_LineRangeToEndOfFile_IndicatesEndOfFile()
    {
        var collection = CreateCollection();
        File.WriteAllLines(Path.Combine(tempRoot, "abc.txt"), ["A", "B", "C"]);

        var result = await collection.ReadFile("abc.txt", startLine: 2);

        Assert.Equal("Lines 2-3 (of 3, end of file):\n2: B\n3: C", result);
    }

    [Fact]
    public async Task ReadFile_LineRangeExceedingReadLimit_ReturnsThePageThatFits()
    {
        var collection = CreateCollection(maxReadCharacters: 20);
        File.WriteAllLines(Path.Combine(tempRoot, "lines.txt"), Enumerable.Range(1, 10).Select(i => $"L{i}"));

        var result = await collection.ReadFile("lines.txt", startLine: 2, endLine: 9);

        // "2: L2\n3: L3\n4: L4" is 17 bytes; a fourth line would make 23
        Assert.Equal("Lines 2-4 (more lines follow; continue with startLine=5):\n2: L2\n3: L3\n4: L4", result);
    }

    [Fact]
    public async Task ReadFile_SingleLineOverReadLimit_ReturnsItCutToTheLimit()
    {
        var collection = CreateCollection(maxReadCharacters: 30);
        File.WriteAllText(Path.Combine(tempRoot, "long.txt"), "short\n" + new string('x', 40) + "\n");

        var result = await collection.ReadFile("long.txt", startLine: 2);

        Assert.Equal("Lines 2-2 (of 2, end of file):\n2: " + new string('x', 27), result);
    }

    [Fact]
    public async Task ReadFile_VeryLongLine_IsCutWithAMarker()
    {
        var collection = CreateCollection();
        File.WriteAllText(Path.Combine(tempRoot, "min.js"), new string('y', 5000) + "\nnext\n");

        var result = await collection.ReadFile("min.js");

        Assert.Equal($"Lines 1-2 (of 2, end of file):\n1: {new string('y', 2000)} [... 3000 more characters]\n2: next", result);
    }

    [Fact]
    public async Task ReadFile_InvalidLineNumbers_ReturnsError()
    {
        var collection = CreateCollection();
        File.WriteAllText(Path.Combine(tempRoot, "test.txt"), "A\nB\nC");

        var r1 = await collection.ReadFile("test.txt", startLine: 0);
        Assert.StartsWith("Error: startLine must be at least 1", r1);

        var r2 = await collection.ReadFile("test.txt", startLine: 3, endLine: 1);
        Assert.StartsWith("Error: endLine", r2);

        var r3 = await collection.ReadFile("test.txt", startLine: 10);
        Assert.StartsWith("Error: startLine (10) is beyond the end of file", r3);
    }

    [Fact]
    public async Task ReadFile_BinaryFile_ReturnsError()
    {
        var collection = CreateCollection();
        var binary = new byte[] { 0x48, 0x65, 0x6C, 0x00, 0x6F }; // contains NUL
        File.WriteAllBytes(Path.Combine(tempRoot, "binary.bin"), binary);

        var result = await collection.ReadFile("binary.bin");

        Assert.StartsWith("Error:", result);
        Assert.Contains("contains binary or non-UTF-8 content", result);
    }

    [Fact]
    public async Task ReadFile_OnDirectory_ReturnsErrorAdvisingListDirectory()
    {
        var collection = CreateCollection();
        Directory.CreateDirectory(Path.Combine(tempRoot, "subdir"));

        var result = await collection.ReadFile("subdir");

        Assert.StartsWith("Error:", result);
        Assert.Contains("Use ListDirectory", result);
    }

    #endregion

    #region CreateFile & WriteFile

    [Fact]
    public async Task CreateFile_CreatesNewFileAndMissingParentDirectories()
    {
        var collection = CreateCollection();

        var result = await collection.CreateFile("nested/deep/new.txt", "hello world");

        Assert.StartsWith("File created successfully:", result);
        var fullPath = Path.Combine(tempRoot, "nested", "deep", "new.txt");
        Assert.True(File.Exists(fullPath));
        Assert.Equal("hello world", File.ReadAllText(fullPath));
    }

    [Fact]
    public async Task CreateFile_ExistingFile_ReturnsErrorAdvisingEditOrWrite()
    {
        var collection = CreateCollection();
        File.WriteAllText(Path.Combine(tempRoot, "exists.txt"), "old");

        var result = await collection.CreateFile("exists.txt", "new");

        Assert.StartsWith("Error: File already exists", result);
        Assert.Contains("Use EditFile", result);
        Assert.Contains("WriteFile", result);
        Assert.Equal("old", File.ReadAllText(Path.Combine(tempRoot, "exists.txt")));
    }

    [Fact]
    public async Task CreateFile_WritesUtf8WithoutByteOrderMark()
    {
        var collection = CreateCollection();

        var result = await collection.CreateFile("new.txt", "café");

        Assert.Equal("File created successfully: 'new.txt' (5 bytes).", result);
        Assert.Equal(new byte[] { 0x63, 0x61, 0x66, 0xC3, 0xA9 }, File.ReadAllBytes(Path.Combine(tempRoot, "new.txt")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WriteFile_KeepsTheByteOrderMarkTheFileHad(bool hadByteOrderMark)
    {
        var collection = CreateCollection();
        var path = Path.Combine(tempRoot, "target.txt");
        File.WriteAllText(path, "old", new UTF8Encoding(hadByteOrderMark));

        await collection.WriteFile("target.txt", "new");

        Assert.Equal(hadByteOrderMark, File.ReadAllBytes(path).AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.Equal("new", File.ReadAllText(path));
    }

    [Fact]
    public async Task WriteFile_ExistingFile_OverwritesContent()
    {
        var collection = CreateCollection();
        File.WriteAllText(Path.Combine(tempRoot, "target.txt"), "old content");

        var result = await collection.WriteFile("target.txt", "new content");

        Assert.StartsWith("File overwritten successfully:", result);
        Assert.Equal("new content", File.ReadAllText(Path.Combine(tempRoot, "target.txt")));
    }

    [Fact]
    public async Task WriteFile_NonExistentFile_ReturnsErrorAdvisingCreateFile()
    {
        var collection = CreateCollection();

        var result = await collection.WriteFile("missing.txt", "content");

        Assert.StartsWith("Error: File not found", result);
        Assert.Contains("Use CreateFile", result);
    }

    #endregion

    #region EditFile

    [Fact]
    public async Task EditFile_ExactUniqueMatch_ReplacesContentAndReturnsRange()
    {
        var collection = CreateCollection();
        var initial = "line 1\nline 2 to edit\nline 3";
        File.WriteAllText(Path.Combine(tempRoot, "edit.txt"), initial);

        var result = await collection.EditFile("edit.txt", "line 2 to edit", "line 2 replaced");

        Assert.Equal("Successfully edited 'edit.txt'. Replaced lines 2-2 (now lines 2-2):\n2: line 2 replaced", result);
        Assert.Equal("line 1\nline 2 replaced\nline 3", File.ReadAllText(Path.Combine(tempRoot, "edit.txt")));
    }

    [Fact]
    public async Task EditFile_PreservesCRLFLineEndings()
    {
        var collection = CreateCollection();
        var initial = "first\r\nsecond\r\nthird\r\n";
        File.WriteAllText(Path.Combine(tempRoot, "crlf.txt"), initial);

        // Edit using LF in target/replacement arguments
        var result = await collection.EditFile("crlf.txt", "second\nthird", "second modified\nthird modified");

        Assert.StartsWith("Successfully edited", result);
        var edited = File.ReadAllText(Path.Combine(tempRoot, "crlf.txt"));
        Assert.Contains("\r\n", edited);
        Assert.Equal("first\r\nsecond modified\r\nthird modified\r\n", edited);
    }

    [Fact]
    public async Task EditFile_MixedLineEndings_LeavesUntouchedLinesAsTheyWere()
    {
        var collection = CreateCollection();
        var path = Path.Combine(tempRoot, "mixed.txt");
        File.WriteAllText(path, "one\ntwo\r\nthree\nfour\n");

        var result = await collection.EditFile("mixed.txt", "two\nthree", "2\n3");

        Assert.StartsWith("Successfully edited", result);
        Assert.Equal("one\n2\r\n3\nfour\n", File.ReadAllText(path));
    }

    [Fact]
    public async Task EditFile_TargetEndingInALineBreak_ReportsOnlyTheLinesItCovers()
    {
        var collection = CreateCollection();
        File.WriteAllText(Path.Combine(tempRoot, "edit.txt"), "a\nb\nc\n");

        var result = await collection.EditFile("edit.txt", "b\n", "b1\nb2\n");

        Assert.Equal("Successfully edited 'edit.txt'. Replaced lines 2-2 (now lines 2-3):\n2: b1\n3: b2", result);
        Assert.Equal("a\nb1\nb2\nc\n", File.ReadAllText(Path.Combine(tempRoot, "edit.txt")));
    }

    [Fact]
    public async Task EditFile_FileWithByteOrderMark_KeepsIt()
    {
        var collection = CreateCollection();
        var path = Path.Combine(tempRoot, "bom.txt");
        File.WriteAllText(path, "first\nsecond", new UTF8Encoding(true));

        await collection.EditFile("bom.txt", "first", "1st");

        Assert.True(File.ReadAllBytes(path).AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.Equal("1st\nsecond", File.ReadAllText(path));
    }

    [Fact]
    public async Task EditFile_TextThatIsNotUtf8_ReturnsErrorAndLeavesTheFileUnchanged()
    {
        var collection = CreateCollection();
        var path = Path.Combine(tempRoot, "latin1.txt");
        byte[] latin1 = [0x63, 0x61, 0x66, 0xE9, 0x0A];
        File.WriteAllBytes(path, latin1);

        var result = await collection.EditFile("latin1.txt", "caf", "tea");

        Assert.Equal("Error: File 'latin1.txt' contains binary or non-UTF-8 content and cannot be edited.", result);
        Assert.Equal(latin1, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task EditFile_ZeroMatches_ReturnsError()
    {
        var collection = CreateCollection();
        File.WriteAllText(Path.Combine(tempRoot, "file.txt"), "some content");

        var result = await collection.EditFile("file.txt", "not found text", "replacement");

        Assert.StartsWith("Error: oldText was not found", result);
        Assert.Contains("Read the file lines again", result);
    }

    [Fact]
    public async Task EditFile_NoMatchButSimilarLines_ShowsThemWithTheirNumbers()
    {
        var collection = CreateCollection();
        File.WriteAllText(Path.Combine(tempRoot, "code.cs"), "class A\n{\n    void Run()\n    {\n    }\n}\n");

        // Indentation differs from the file's
        var result = await collection.EditFile("code.cs", "void   Run() {", "void Go() {");

        Assert.StartsWith("Error: oldText was not found in 'code.cs'. The closest lines in the file are:\n3:     void Run()", result);
        Assert.Contains("leaving out the 'N: ' prefixes", result);
    }

    [Fact]
    public async Task EditFile_MultipleMatches_ReturnsCountAndLineNumbers()
    {
        var collection = CreateCollection();
        var content = "match\nother\nmatch\nend";
        File.WriteAllText(Path.Combine(tempRoot, "multi.txt"), content);

        var result = await collection.EditFile("multi.txt", "match", "new");

        Assert.StartsWith("Error: oldText matched 2 times", result);
        Assert.Contains("line 1", result);
        Assert.Contains("line 3", result);
        Assert.Contains("Add surrounding lines", result);
        Assert.Contains("replaceAll", result);
        Assert.Equal(content, File.ReadAllText(Path.Combine(tempRoot, "multi.txt")));
    }

    [Fact]
    public async Task EditFile_ReplaceAll_ChangesEveryOccurrenceAndNamesTheLines()
    {
        var collection = CreateCollection();
        var path = Path.Combine(tempRoot, "multi.txt");
        File.WriteAllText(path, "match\r\nother\r\nmatch\r\nend");

        var result = await collection.EditFile("multi.txt", "match", "new", replaceAll: true);

        Assert.Equal("Successfully edited 'multi.txt'. Replaced 2 occurrences at line 1, line 3.", result);
        Assert.Equal("new\r\nother\r\nnew\r\nend", File.ReadAllText(path));
    }

    [Fact]
    public async Task EditFile_ReplaceAllWithNoMatch_ReturnsError()
    {
        var collection = CreateCollection();
        File.WriteAllText(Path.Combine(tempRoot, "file.txt"), "some content");

        var result = await collection.EditFile("file.txt", "absent", "x", replaceAll: true);

        Assert.StartsWith("Error: oldText was not found", result);
    }

    [Fact]
    public async Task EditFile_LongReplacement_EchoesOnlyTheFirstLines()
    {
        var collection = CreateCollection();
        File.WriteAllText(Path.Combine(tempRoot, "edit.txt"), "a\nb\nc");

        var result = await collection.EditFile("edit.txt", "b", string.Join("\n", Enumerable.Range(1, 30).Select(i => $"n{i}")));

        Assert.StartsWith("Successfully edited 'edit.txt'. Replaced lines 2-2 (now lines 2-31):\n2: n1\n", result);
        Assert.EndsWith("21: n20\n[... 10 more lines]", result);
    }

    [Fact]
    public async Task EditFile_EmptyTargetOrIdenticalReplacement_ReturnsError()
    {
        var collection = CreateCollection();
        File.WriteAllText(Path.Combine(tempRoot, "file.txt"), "content");

        var r1 = await collection.EditFile("file.txt", "", "replacement");
        Assert.Equal("Error: oldText cannot be empty.", r1);

        var r2 = await collection.EditFile("file.txt", "content", "content");
        Assert.Contains("identical", r2);
    }

    #endregion

    #region MoveFile & DeleteFile

    [Fact]
    public async Task MoveFile_ValidMove_MovesFileAndCreatesParentDirs()
    {
        var collection = CreateCollection();
        File.WriteAllText(Path.Combine(tempRoot, "source.txt"), "data");

        var result = await collection.MoveFile("source.txt", "dest/nested/renamed.txt");

        Assert.StartsWith("Successfully moved", result);
        Assert.False(File.Exists(Path.Combine(tempRoot, "source.txt")));
        Assert.True(File.Exists(Path.Combine(tempRoot, "dest", "nested", "renamed.txt")));
    }

    [Fact]
    public async Task MoveFile_DestinationExists_ReturnsErrorWithoutOverwriting()
    {
        var collection = CreateCollection();
        File.WriteAllText(Path.Combine(tempRoot, "source.txt"), "source data");
        File.WriteAllText(Path.Combine(tempRoot, "dest.txt"), "dest data");

        var result = await collection.MoveFile("source.txt", "dest.txt");

        Assert.StartsWith("Error: Destination file already exists", result);
        Assert.Contains("will not overwrite", result);
        Assert.Equal("dest data", File.ReadAllText(Path.Combine(tempRoot, "dest.txt")));
        Assert.True(File.Exists(Path.Combine(tempRoot, "source.txt")));
    }

    [Fact]
    public async Task MoveFile_SourceIsDirectory_ReturnsError()
    {
        var collection = CreateCollection();
        Directory.CreateDirectory(Path.Combine(tempRoot, "src_dir"));

        var result = await collection.MoveFile("src_dir", "dst_dir");

        Assert.StartsWith("Error: 'src_dir' is a directory, not a file.", result);
    }

    [Fact]
    public async Task DeleteFile_ValidFile_DeletesFile()
    {
        var collection = CreateCollection();
        var filePath = Path.Combine(tempRoot, "to_delete.txt");
        File.WriteAllText(filePath, "data");

        var result = await collection.DeleteFile("to_delete.txt");

        Assert.StartsWith("Successfully deleted file", result);
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public async Task DeleteFile_OnALink_DeletesTheLinkAndKeepsItsTarget()
    {
        var collection = CreateCollection();
        var target = Path.Combine(tempRoot, "target.txt");
        var link = Path.Combine(tempRoot, "link.txt");
        File.WriteAllText(target, "data");
        SymbolicLinks.ToFile(link, target);

        var result = await collection.DeleteFile("link.txt");

        Assert.StartsWith("Successfully deleted file", result);
        Assert.Null(new FileInfo(link).LinkTarget);
        Assert.False(File.Exists(link));
        Assert.Equal("data", File.ReadAllText(target));
    }

    [Fact]
    public async Task MoveFile_OnALink_MovesTheLinkAndKeepsItsTarget()
    {
        var collection = CreateCollection();
        var target = Path.Combine(tempRoot, "target.txt");
        File.WriteAllText(target, "data");
        SymbolicLinks.ToFile(Path.Combine(tempRoot, "link.txt"), target);

        var result = await collection.MoveFile("link.txt", "renamed.txt");

        Assert.StartsWith("Successfully moved", result);
        Assert.Equal("data", File.ReadAllText(target));
        Assert.NotNull(new FileInfo(Path.Combine(tempRoot, "renamed.txt")).LinkTarget);
    }

    [Fact]
    public async Task DeleteFile_OnDirectory_ReturnsError()
    {
        var collection = CreateCollection();
        Directory.CreateDirectory(Path.Combine(tempRoot, "dir_to_delete"));

        var result = await collection.DeleteFile("dir_to_delete");

        Assert.StartsWith("Error: 'dir_to_delete' is a directory, not a file.", result);
    }

    [Fact]
    public async Task DeleteFile_NonExistentFile_ReturnsError()
    {
        var collection = CreateCollection();

        var result = await collection.DeleteFile("missing.txt");

        Assert.StartsWith("Error: File not found", result);
    }

    #endregion

    #region Path Sandboxing on Tools

    [Theory]
    [InlineData("../../outside.txt")]
    [InlineData("/etc/passwd")]
    public async Task Tools_RejectOutOfBoundPaths(string maliciousPath)
    {
        var collection = CreateCollection();

        var rList = await collection.ListDirectory(maliciousPath);
        Assert.Contains("must stay inside the base directory", rList);

        var rRead = await collection.ReadFile(maliciousPath);
        Assert.Contains("must stay inside the base directory", rRead);

        var rCreate = await collection.CreateFile(maliciousPath, "test");
        Assert.Contains("must stay inside the base directory", rCreate);

        var rWrite = await collection.WriteFile(maliciousPath, "test");
        Assert.Contains("must stay inside the base directory", rWrite);

        var rEdit = await collection.EditFile(maliciousPath, "a", "b");
        Assert.Contains("must stay inside the base directory", rEdit);

        var rMove = await collection.MoveFile(maliciousPath, "valid.txt");
        Assert.Contains("must stay inside the base directory", rMove);

        var rDelete = await collection.DeleteFile(maliciousPath);
        Assert.Contains("must stay inside the base directory", rDelete);
    }

    [Fact]
    public async Task Tools_RejectALinkLeadingThroughAnotherLinkOutsideBase()
    {
        using var outside = new TempDirectory("filetools-outside-");
        var secret = Path.Combine(outside.Path, "secret.txt");
        File.WriteAllText(secret, "secret");
        SymbolicLinks.ToDirectory(Path.Combine(tempRoot, "exit"), outside.Path);
        SymbolicLinks.ToFile(Path.Combine(tempRoot, "x"), Path.Combine("exit", "secret.txt"));

        var collection = CreateCollection();

        Assert.Contains("traverses outside the base directory", await collection.ReadFile("x"));
        Assert.Contains("traverses outside the base directory", await collection.WriteFile("x", "changed"));
        Assert.Contains("traverses outside the base directory", await collection.EditFile("x", "secret", "changed"));
        Assert.Contains("traverses outside the base directory", await collection.CreateFile("exit/new.txt", "new"));
        Assert.Equal("secret", File.ReadAllText(secret));
        Assert.False(File.Exists(Path.Combine(outside.Path, "new.txt")));
    }

    #endregion
}
