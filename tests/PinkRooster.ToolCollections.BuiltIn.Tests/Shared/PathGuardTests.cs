using PinkRooster.ToolCollections.BuiltIn.Shared;
using PinkRooster.ToolCollections.BuiltIn.Tests.TestSupport;

namespace PinkRooster.ToolCollections.BuiltIn.Tests.FileOperations;

public sealed class PathGuardTests : IDisposable
{
    private readonly TempDirectory temp = new("pathguard-test-");

    private string tempRoot => temp.Path;

    public void Dispose() => temp.Dispose();

    [Fact]
    public void CanonicalizeBaseDirectory_ValidDirectory_ReturnsCanonicalPath()
    {
        var canonical = PathGuard.CanonicalizeBaseDirectory(tempRoot);

        // Not compared with the temp path itself: on macOS the temp folder sits behind a link, which is resolved here
        Assert.True(Path.IsPathFullyQualified(canonical));
        Assert.True(Directory.Exists(canonical));
        Assert.Equal(Path.GetFileName(tempRoot), Path.GetFileName(canonical));
    }

    [Fact]
    public void CanonicalizeBaseDirectory_BehindALinkedParent_ResolvesTheLink()
    {
        var realParent = Path.Combine(tempRoot, "real");
        Directory.CreateDirectory(Path.Combine(realParent, "base"));
        var linkedParent = Path.Combine(tempRoot, "linked");
        SymbolicLinks.ToDirectory(linkedParent, realParent);

        var canonical = PathGuard.CanonicalizeBaseDirectory(Path.Combine(linkedParent, "base"));

        Assert.Equal(PathGuard.CanonicalizeBaseDirectory(Path.Combine(realParent, "base")), canonical);
    }

    [Fact]
    public void CanonicalizeBaseDirectory_NonExistent_ThrowsArgumentExceptionNamingPath()
    {
        var missingPath = Path.Combine(tempRoot, "missing_subdir_12345");

        var ex = Assert.Throws<ArgumentException>(() => PathGuard.CanonicalizeBaseDirectory(missingPath));
        Assert.Contains(missingPath, ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CanonicalizeBaseDirectory_NullOrWhitespace_ThrowsArgumentException(string? invalidPath)
    {
        Assert.Throws<ArgumentException>(() => PathGuard.CanonicalizeBaseDirectory(invalidPath));
    }

    [Fact]
    public void TryResolvePath_RelativePathInsideBase_ResolvesCorrectly()
    {
        var canonicalBase = PathGuard.CanonicalizeBaseDirectory(tempRoot);

        bool success = PathGuard.TryResolvePath(canonicalBase, "sub/file.txt", out string resolved, out string? error);

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal(Path.Combine(canonicalBase, "sub", "file.txt"), resolved);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("./")]
    public void TryResolvePath_Dot_ResolvesToBaseDirectory(string dotPath)
    {
        var canonicalBase = PathGuard.CanonicalizeBaseDirectory(tempRoot);

        bool success = PathGuard.TryResolvePath(canonicalBase, dotPath, out string resolved, out string? error);

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal(canonicalBase, resolved);
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("../")]
    [InlineData("a/../../outside.txt")]
    public void TryResolvePath_ParentTraversalSequence_ReturnsError(string traversalPath)
    {
        var canonicalBase = PathGuard.CanonicalizeBaseDirectory(tempRoot);

        bool success = PathGuard.TryResolvePath(canonicalBase, traversalPath, out string resolved, out string? error);

        Assert.False(success);
        Assert.Empty(resolved);
        Assert.NotNull(error);
        Assert.StartsWith("Error:", error);
        Assert.Contains("must stay inside the base directory", error);
    }

    [Fact]
    public void TryResolvePath_AbsoluteOutOfBoundPath_ReturnsError()
    {
        var canonicalBase = PathGuard.CanonicalizeBaseDirectory(tempRoot);
        var outsidePath = Path.GetFullPath(Path.Combine(tempRoot, "..", "completely_outside.txt"));

        bool success = PathGuard.TryResolvePath(canonicalBase, outsidePath, out string resolved, out string? error);

        Assert.False(success);
        Assert.Empty(resolved);
        Assert.NotNull(error);
        Assert.StartsWith("Error:", error);
        Assert.Contains("must stay inside the base directory", error);
    }

    [Fact]
    public void TryResolvePath_SiblingDirectoryPrefixCollision_ReturnsError()
    {
        // If base is /path/test, a sibling /path/test-sibling shares the prefix string,
        // but must be rejected because it is not within the directory boundary.
        var baseDir = Path.Combine(tempRoot, "base");
        Directory.CreateDirectory(baseDir);
        var canonicalBase = PathGuard.CanonicalizeBaseDirectory(baseDir);

        var siblingPath = Path.Combine(tempRoot, "base-sibling", "file.txt");

        bool success = PathGuard.TryResolvePath(canonicalBase, siblingPath, out string resolved, out string? error);

        Assert.False(success);
        Assert.Empty(resolved);
        Assert.NotNull(error);
        Assert.StartsWith("Error:", error);
        Assert.Contains("must stay inside the base directory", error);
    }

    [Fact]
    public void TryResolvePath_SymlinkFilePointingOutsideBase_ReturnsError()
    {
        using var outside = new TempDirectory("outside-");
        var outsideDir = outside.Path;
        var targetFile = Path.Combine(outsideDir, "secret.txt");
        File.WriteAllText(targetFile, "secret");

        var symlinkFile = Path.Combine(tempRoot, "link_to_secret.txt");
        SymbolicLinks.ToFile(symlinkFile, targetFile);

        var canonicalBase = PathGuard.CanonicalizeBaseDirectory(tempRoot);

        bool success = PathGuard.TryResolvePath(canonicalBase, "link_to_secret.txt", out string resolved, out string? error);

        Assert.False(success);
        Assert.Empty(resolved);
        Assert.NotNull(error);
        Assert.StartsWith("Error:", error);
        Assert.Contains("traverses outside the base directory", error);
    }

    [Fact]
    public void TryResolvePath_SymlinkDirectoryPointingOutsideBase_ReturnsError()
    {
        using var outside = new TempDirectory("outside-dir-");
        var outsideDir = outside.Path;
        var targetFile = Path.Combine(outsideDir, "target.txt");
        File.WriteAllText(targetFile, "data");

        var symlinkDir = Path.Combine(tempRoot, "link_dir");
        SymbolicLinks.ToDirectory(symlinkDir, outsideDir);

        var canonicalBase = PathGuard.CanonicalizeBaseDirectory(tempRoot);

        bool success = PathGuard.TryResolvePath(canonicalBase, "link_dir/target.txt", out string resolved, out string? error);

        Assert.False(success);
        Assert.Empty(resolved);
        Assert.NotNull(error);
        Assert.StartsWith("Error:", error);
        Assert.Contains("traverses outside the base directory", error);
    }

    [Fact]
    public void TryResolvePath_SymlinkInsideBasePointingInsideBase_Succeeds()
    {
        var insideSubdir = Path.Combine(tempRoot, "real_sub");
        Directory.CreateDirectory(insideSubdir);
        var targetFile = Path.Combine(insideSubdir, "data.txt");
        File.WriteAllText(targetFile, "hello");

        var symlinkDir = Path.Combine(tempRoot, "link_sub");
        SymbolicLinks.ToDirectory(symlinkDir, insideSubdir);

        var canonicalBase = PathGuard.CanonicalizeBaseDirectory(tempRoot);

        bool success = PathGuard.TryResolvePath(canonicalBase, "link_sub/data.txt", out string resolved, out string? error);

        Assert.True(success);
        Assert.Null(error);
        Assert.True(File.Exists(resolved));
    }

    [Fact]
    public void TryResolvePath_LinkLeadingThroughAnotherLinkOutsideBase_ReturnsError()
    {
        // base/x -> exit/secret.txt and base/exit -> outside: the first link's target reads as inside the base directory
        using var outside = new TempDirectory("outside-chain-");
        var outsideDir = outside.Path;
        File.WriteAllText(Path.Combine(outsideDir, "secret.txt"), "secret");
        SymbolicLinks.ToDirectory(Path.Combine(tempRoot, "exit"), outsideDir);

        File.CreateSymbolicLink(Path.Combine(tempRoot, "x"), Path.Combine("exit", "secret.txt"));
        var canonicalBase = PathGuard.CanonicalizeBaseDirectory(tempRoot);

        bool success = PathGuard.TryResolvePath(canonicalBase, "x", out string resolved, out string? error);

        Assert.False(success);
        Assert.Empty(resolved);
        Assert.Contains("traverses outside the base directory", error);
    }

    [Fact]
    public void TryResolvePath_LinkTargetClimbingOutWithParentSegments_ReturnsError()
    {
        Directory.CreateDirectory(Path.Combine(tempRoot, "base", "sub"));
        File.WriteAllText(Path.Combine(tempRoot, "outside.txt"), "outside");
        SymbolicLinks.ToFile(Path.Combine(tempRoot, "base", "sub", "up"), Path.Combine("..", "..", "outside.txt"));

        var canonicalBase = PathGuard.CanonicalizeBaseDirectory(Path.Combine(tempRoot, "base"));

        bool success = PathGuard.TryResolvePath(canonicalBase, "sub/up", out _, out string? error);

        Assert.False(success);
        Assert.Contains("traverses outside the base directory", error);
    }

    [Fact]
    public void TryResolvePath_DanglingLinkPointingOutsideBase_ReturnsError()
    {
        SymbolicLinks.ToFile(Path.Combine(tempRoot, "dangling"), Path.Combine(Path.GetTempPath(), "pathguard-missing-" + Guid.NewGuid().ToString("N")));

        var canonicalBase = PathGuard.CanonicalizeBaseDirectory(tempRoot);

        bool success = PathGuard.TryResolvePath(canonicalBase, "dangling", out _, out string? error);

        Assert.False(success);
        Assert.Contains("traverses outside the base directory", error);
    }

    [Fact]
    public void TryResolvePath_LinksFormingALoop_ReturnsError()
    {
        SymbolicLinks.ToFile(Path.Combine(tempRoot, "a"), "b");
        SymbolicLinks.ToFile(Path.Combine(tempRoot, "b"), "a");

        var canonicalBase = PathGuard.CanonicalizeBaseDirectory(tempRoot);

        bool success = PathGuard.TryResolvePath(canonicalBase, "a", out _, out string? error);

        Assert.False(success);
        Assert.StartsWith("Error: Unable to resolve symbolic link", error);
    }

    [Fact]
    public void TryResolvePath_NotFollowingTheFinalLink_ReturnsTheLinkItself()
    {
        using var outside = new TempDirectory("outside-final-");
        var outsideDir = outside.Path;
        var linkPath = Path.Combine(tempRoot, "link.txt");
        SymbolicLinks.ToFile(linkPath, Path.Combine(outsideDir, "target.txt"));

        var canonicalBase = PathGuard.CanonicalizeBaseDirectory(tempRoot);

        bool success = PathGuard.TryResolvePath(canonicalBase, "link.txt", out string resolved, out string? error, followFinalLink: false);

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal(Path.Combine(canonicalBase, "link.txt"), resolved);
    }

    [Fact]
    public void TryResolvePath_NotFollowingTheFinalLink_StillResolvesLinkedParents()
    {
        using var outside = new TempDirectory("outside-parent-");
        var outsideDir = outside.Path;
        SymbolicLinks.ToDirectory(Path.Combine(tempRoot, "exit"), outsideDir);

        var canonicalBase = PathGuard.CanonicalizeBaseDirectory(tempRoot);

        bool success = PathGuard.TryResolvePath(canonicalBase, "exit/file.txt", out _, out string? error, followFinalLink: false);

        Assert.False(success);
        Assert.Contains("traverses outside the base directory", error);
    }
}
