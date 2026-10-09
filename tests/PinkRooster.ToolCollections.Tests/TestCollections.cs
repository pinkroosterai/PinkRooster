
namespace PinkRooster.ToolCollections.Tests;

/// <summary>Small collections the context tests share.</summary>
internal static class TestCollections
{
    public sealed class Counter : ToolCollection
    {
        private int count;

        [Tool("Increment", "Adds one to the counter.")]
        public string Increment() => $"{Interlocked.Increment(ref count)}";

        public override ValueTask<string?> GetContextAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>($"## Counter\n{Volatile.Read(ref count)}");
    }

    public sealed class Note(string note) : ToolCollection
    {
        public override ValueTask<string?> GetContextAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>(note);
    }

    [ToolCollectionInstruction("Nothing is ever done.")]
    [ToolCollectionConstraint("Never do anything.")]
    public sealed class Silent : ToolCollection
    {
        [Tool("Nothing", "Does nothing.")]
        public string Nothing() => "ok";
    }

    public sealed class Failing : ToolCollection
    {
        public const string Message = "context broke";

        public override ValueTask<string?> GetContextAsync(CancellationToken cancellationToken) => throw new InvalidOperationException(Message);
    }

    public sealed class Cancelling(CancellationTokenSource cancellation) : ToolCollection
    {
        public override ValueTask<string?> GetContextAsync(CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<string?>(null);
        }
    }

    public sealed class Guarded : ToolCollection
    {
        public int Deletes { get; private set; }

        [Tool("Delete", "Deletes the file.", RequiresApproval = true)]
        public string Delete() => $"deleted {++Deletes}";
    }
}
