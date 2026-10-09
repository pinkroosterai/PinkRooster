using PinkRooster.ToolCollections.BuiltIn.FileOperations;
using PinkRooster.ToolCollections.BuiltIn.Shared;
using PinkRooster.ToolCollections.BuiltIn.Tests.TestSupport;

namespace PinkRooster.ToolCollections.BuiltIn.Tests.FileOperations;

public sealed class FileReadToolCollectionBuilderTests : IDisposable
{
    private readonly TempDirectory temp = new("filereadbuilder-test-");

    public void Dispose() => temp.Dispose();

    [Fact]
    public void Build_PutsTheWorkspaceAndTheReadLimitIntoTheCollection()
    {
        FileReadToolCollection built = new FileReadToolCollectionBuilder()
            .InWorkspace(new Workspace(temp.Path))
            .WithMaxReadCharacters(2048)
            .Build();

        Assert.Equal(2048, built.MaxReadCharacters);
        Assert.Contains(built.Instructions, text => text.Contains(built.BaseDirectory));
    }

    [Fact]
    public void Build_WithoutAWorkspace_ThrowsNamingTheFix()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => new FileReadToolCollectionBuilder().Build());

        Assert.Contains("InWorkspace", ex.Message);
    }

    [Fact]
    public void Methods_RefuseBadArgumentsAtTheCall()
    {
        FileReadToolCollectionBuilder builder = new();

        Assert.Throws<ArgumentNullException>(() => builder.InWorkspace(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithMaxReadCharacters(-1));
    }
}
