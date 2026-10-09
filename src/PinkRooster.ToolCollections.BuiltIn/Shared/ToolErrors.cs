namespace PinkRooster.ToolCollections.BuiltIn.Shared;

/// <summary>
/// Runs a tool body and turns what the operating system refuses into the text the model reads. Anything else is a bug in
/// the collection and is left to throw, as is a cancellation.
/// </summary>
internal static class ToolErrors
{
    public static async Task<string> RunAsync(Func<Task<string>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (Exception ex) when (IsFileSystemFailure(ex))
        {
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>As <see cref="RunAsync"/>, for a body that blocks, on a pool thread.</summary>
    public static Task<string> RunOnPoolAsync(Func<string> action, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return action();
            }
            catch (Exception ex) when (IsFileSystemFailure(ex))
            {
                return $"Error: {ex.Message}";
            }
        }, cancellationToken);

    // What the operating system refuses (access denied, file in use, a bad name).
    private static bool IsFileSystemFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException;
}
