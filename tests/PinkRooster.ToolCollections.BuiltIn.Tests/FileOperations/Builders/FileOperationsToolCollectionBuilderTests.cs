using PinkRooster.ToolCollections.BuiltIn.FileOperations;
using PinkRooster.ToolCollections.BuiltIn.Shared;
using PinkRooster.ToolCollections.BuiltIn.Tests.TestSupport;

namespace PinkRooster.ToolCollections.BuiltIn.Tests.FileOperations;

public sealed class FileOperationsToolCollectionBuilderTests : IDisposable
{
    private readonly TempDirectory temp = new("filebuilder-test-");

    public void Dispose() => temp.Dispose();

    [Fact]
    public void Build_PutsEverySettingIntoTheCollection()
    {
        FileOperationsToolCollection built = new FileOperationsToolCollectionBuilder()
            .InWorkspace(new Workspace(temp.Path))
            .WithMaxReadCharacters(1024)
            .KeepBackups()
            .Build();

        Assert.Equal(1024, built.MaxReadCharacters);
        Assert.Contains(built.Instructions, text => text.Contains(".pinkrooster/backups"));
    }

    [Fact]
    public void Build_WithoutBackups_AddsNoBackupInstruction()
    {
        FileOperationsToolCollection built = new FileOperationsToolCollectionBuilder().InWorkspace(new Workspace(temp.Path)).Build();

        Assert.Equal(FileReadToolCollection.DefaultMaxReadCharacters, built.MaxReadCharacters);
        Assert.DoesNotContain(built.Instructions, text => text.Contains(".pinkrooster/backups"));
    }

    [Fact]
    public void Build_CalledTwice_GivesIndependentCollections()
    {
        FileOperationsToolCollectionBuilder builder = new FileOperationsToolCollectionBuilder().InWorkspace(new Workspace(temp.Path));

        Assert.NotSame(builder.Build(), builder.Build());
    }

    [Fact]
    public void Build_WithoutAWorkspace_ThrowsNamingTheFix()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => new FileOperationsToolCollectionBuilder().Build());

        Assert.Contains("InWorkspace", ex.Message);
    }

    [Fact]
    public void Methods_RefuseBadArgumentsAtTheCall()
    {
        FileOperationsToolCollectionBuilder builder = new();

        Assert.Throws<ArgumentNullException>(() => builder.InWorkspace(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithMaxReadCharacters(0));
    }
}
