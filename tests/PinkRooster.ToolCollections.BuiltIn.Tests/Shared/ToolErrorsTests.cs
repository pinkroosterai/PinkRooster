using PinkRooster.ToolCollections.BuiltIn.Shared;

namespace PinkRooster.ToolCollections.BuiltIn.Tests.Shared;

public sealed class ToolErrorsTests
{
    [Fact]
    public async Task RunAsync_IoFailure_BecomesErrorText()
    {
        string result = await ToolErrors.RunAsync(() => throw new IOException("disk gone"));

        Assert.Equal("Error: disk gone", result);
    }

    [Fact]
    public async Task RunOnPoolAsync_AccessDenied_BecomesErrorText()
    {
        string result = await ToolErrors.RunOnPoolAsync(() => throw new UnauthorizedAccessException("no"), CancellationToken.None);

        Assert.Equal("Error: no", result);
    }

    [Fact]
    public async Task RunAsync_ABug_IsNotHidden()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => ToolErrors.RunAsync(() => throw new InvalidOperationException("bug")));
    }

    [Fact]
    public async Task RunOnPoolAsync_Cancellation_Throws()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ToolErrors.RunOnPoolAsync(() => "x", cancelled.Token));
    }
}
