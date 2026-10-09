using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PinkRooster.ToolCollections.BuiltIn.TaskLists;

/// <summary>Keeps the task list in one JSON file.</summary>
/// <remarks>
/// A save writes a temporary file and renames it over the real one, so a crash never leaves a half-written list. It does not
/// guard against two processes writing the same file: the last one wins. Use one file per list, in one process. A file that
/// cannot be read is moved aside to <c>&lt;path&gt;.corrupt</c> and logged, and the list starts empty, so nothing is overwritten unseen.
/// </remarks>
public sealed class JsonFileTaskListStorage : ITaskListStorage
{
    private readonly string path;
    private readonly ILogger? logger;

    /// <param name="path">The file to keep the list in. It and its directory are created on the first save.</param>
    /// <param name="logger">Where an unreadable file is reported.</param>
    public JsonFileTaskListStorage(string path, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = Path.GetFullPath(path);
        this.logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<TaskListSnapshot?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
            return await JsonSerializer.DeserializeAsync(stream, TaskListJsonContext.Default.TaskListSnapshot, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException error)
        {
            string aside = path + ".corrupt";
            File.Move(path, aside, overwrite: true);
            logger?.LogError(error, "The task list file {Path} could not be read; it was moved to {Aside} and the list starts empty.", path, aside);
            return null;
        }
    }

    /// <inheritdoc />
    public async ValueTask SaveAsync(TaskListSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporary = path + ".tmp";
        await using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, snapshot, TaskListJsonContext.Default.TaskListSnapshot, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporary, path, overwrite: true);
    }
}
