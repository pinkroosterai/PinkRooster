using PinkRooster.ToolCollections.BuiltIn.Images;
using PinkRooster.ToolCollections.BuiltIn.Tests.TestSupport;

namespace PinkRooster.ToolCollections.BuiltIn.Tests.Images;

public sealed class ImageToolCollectionBuilderTests : IDisposable
{
    private readonly TempDirectory temp = new("image-builder-test-");

    public void Dispose() => temp.Dispose();

    [Fact]
    public void Build_WithoutAWorkspace_ThrowsNamingTheCall()
    {
        var builder = new ImageToolCollectionBuilder().WithVisionClient(new UnansweringChatClient());

        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("InWorkspace(", ex.Message);
    }

    [Fact]
    public void Build_WithoutAVisionClient_ThrowsNamingTheCall()
    {
        var builder = new ImageToolCollectionBuilder().InWorkspace(new Workspace(temp.Path));

        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("WithVisionClient(", ex.Message);
    }

    [Fact]
    public void Build_CalledTwice_GivesTwoCollections()
    {
        var builder = new ImageToolCollectionBuilder().InWorkspace(new Workspace(temp.Path)).WithVisionClient(new UnansweringChatClient());

        Assert.NotSame(builder.Build(), builder.Build());
    }

    [Fact]
    public void Limits_OutOfRange_AreRefused()
    {
        var builder = new ImageToolCollectionBuilder();

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithMaxImageBytes(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithMaxImagesPerCall(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithTimeout(TimeSpan.Zero));
    }
}
