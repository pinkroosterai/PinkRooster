using PinkRooster.ToolCollections.BuiltIn.Shared;
using PinkRooster.ToolCollections.BuiltIn.Tests.TestSupport;

namespace PinkRooster.ToolCollections.BuiltIn.Tests;

public sealed class WorkspaceTests : IDisposable
{
    private readonly TempDirectory temp = new("workspace-test-");

    public void Dispose() => temp.Dispose();

    [Fact]
    public void Constructor_ExistingFolder_HoldsItsCanonicalRoot()
    {
        var workspace = new Workspace(temp.Path + Path.DirectorySeparatorChar);

        Assert.Equal(PathGuard.CanonicalizeBaseDirectory(temp.Path), workspace.RootDirectory);
        Assert.DoesNotContain(workspace.RootDirectory[^1], "/\\");
    }

    [Fact]
    public void Constructor_NoSkippedFolders_UsesTheDefaults()
    {
        var workspace = new Workspace(temp.Path);

        Assert.Equal([".git", "node_modules", "bin", "obj", ".pinkrooster"], workspace.SkippedDirectories);
    }

    [Fact]
    public void Constructor_SkippedFoldersGiven_KeepsThem()
    {
        var workspace = new Workspace(temp.Path, ["vendor"]);

        Assert.Equal(["vendor"], workspace.SkippedDirectories);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_BlankRoot_Throws(string root)
    {
        Assert.Throws<ArgumentException>(() => new Workspace(root));
    }

    [Fact]
    public void Constructor_MissingRoot_ThrowsNamingIt()
    {
        string missing = Path.Combine(temp.Path, "missing");

        var ex = Assert.Throws<ArgumentException>(() => new Workspace(missing));

        Assert.Contains(missing, ex.Message);
    }
}
