using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace PinkRooster.Agents.Sessions;

/// <summary>
/// Saves an agent's sessions as files in one folder, lists them and restores one, so a conversation can be gone on with after the
/// program has ended. It stores what MAF's <c>SerializeSessionAsync</c> returns, with the first prompt, the time and the agent's tool names.
/// </summary>
/// <remarks>
/// <para>
/// Save after every run: <see cref="SaveAsync"/> writes one file per session and replaces it each time, so the file is the session as
/// it was after the last run that ended. Restore with the same agent configuration that saved it; <see cref="RestoreAsync"/> restores
/// also when the tools differ, and says which.
/// </para>
/// <para>
/// A file holds the whole conversation, tool results included, as plain JSON: keep the folder out of version control and away from
/// other users. The store never deletes a file.
/// </para>
/// </remarks>
public sealed class SessionStore
{
    private const string RecordKey = "PinkRooster.Agents.Sessions.SessionRecord";

    private readonly TimeProvider time;

    /// <param name="directory">The folder the session files are kept in, such as <c>.pinkrooster/sessions</c> in a workspace. It is created at the first save.</param>
    /// <param name="timeProvider">The clock for a file's time and a new session's id; the system clock when null.</param>
    /// <exception cref="ArgumentException">The folder's path is blank.</exception>
    public SessionStore(string directory, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory = Path.GetFullPath(directory);
        time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The folder the session files are kept in.</summary>
    public string Directory { get; }

    /// <summary>
    /// Writes <paramref name="session"/> to its file, replacing what was there. The first save of a session gives it an id and keeps
    /// <paramref name="prompt"/> as its first prompt; later saves keep both.
    /// </summary>
    /// <remarks>Call it after a run has ended, not during one. The file is written beside its place and moved there, so a reader never sees half a file.</remarks>
    /// <param name="agent">The agent the session belongs to.</param>
    /// <param name="session">The session to save.</param>
    /// <param name="prompt">The prompt of the run that just ended. Only a session's first save uses it.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>The file as the list shows it.</returns>
    /// <exception cref="ArgumentException">The prompt is blank on a session's first save.</exception>
    public async Task<SavedSession> SaveAsync(AIAgent agent, AgentSession session, string prompt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(session);

        DateTimeOffset now = time.GetUtcNow();
        if (!session.StateBag.TryGetValue(RecordKey, out SessionRecord? record) || record is null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
            record = new SessionRecord { Id = $"{now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}", FirstPrompt = prompt.Trim() };
            // In the session, so it is in the file too: a restored session is saved to the file it came from.
            session.StateBag.SetValue(RecordKey, record);
        }

        JsonElement serialized = await agent.SerializeSessionAsync(session, cancellationToken: cancellationToken).ConfigureAwait(false);
        string[] tools = ToolNames(agent);
        System.IO.Directory.CreateDirectory(Directory);
        string path = PathOf(record.Id);
        string draft = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            FileStream stream = new(draft, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);
            await using (stream.ConfigureAwait(false))
            {
                Utf8JsonWriter writer = new(stream);
                await using (writer.ConfigureAwait(false))
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", record.Id);
                    writer.WriteString("firstPrompt", record.FirstPrompt);
                    writer.WriteString("savedAt", now);
                    writer.WriteStartArray("tools");
                    foreach (string tool in tools)
                    {
                        writer.WriteStringValue(tool);
                    }
                    writer.WriteEndArray();
                    writer.WritePropertyName("session");
                    serialized.WriteTo(writer);
                    writer.WriteEndObject();
                    await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            File.Move(draft, path, overwrite: true);
        }
        finally
        {
            File.Delete(draft);
        }
        return new SavedSession(record.Id, record.FirstPrompt, now, tools, path);
    }

    /// <summary>
    /// Lists the saved sessions, the one saved last first. A file that cannot be read is in the list too, with
    /// <see cref="SavedSession.IsReadable"/> false and the reason, and stays on disk.
    /// </summary>
    /// <returns>The sessions; empty when the folder does not exist or holds none.</returns>
    public IReadOnlyList<SavedSession> List()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        List<SavedSession> sessions = [];
        foreach (string path in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
        {
            sessions.Add(Read(path, out _));
        }
        return [.. sessions.OrderByDescending(session => session.SavedAt).ThenByDescending(session => session.Id, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Reads the session saved under <paramref name="id"/> back for <paramref name="agent"/>, and reports the tools that are gone
    /// and the tools that are new since it was saved. A session is restored whether or not the tools differ.
    /// </summary>
    /// <remarks>
    /// The session's next run sends the saved messages followed by the new prompt. A call the model makes to a tool that is gone is
    /// answered by the tool loop as an unknown tool.
    /// </remarks>
    /// <param name="agent">The agent to restore the session for, configured as the one that saved it.</param>
    /// <param name="id">The session's id, from <see cref="List"/>.</param>
    /// <param name="cancellationToken">Cancels the restore.</param>
    /// <exception cref="ArgumentException">No session is saved under the id; the message lists the ids there are.</exception>
    /// <exception cref="InvalidOperationException">The file cannot be read, or the agent cannot read the session in it; the file is left where it is.</exception>
    public async Task<RestoredSession> RestoreAsync(AIAgent agent, string id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        string path = PathOf(id);
        if (!File.Exists(path))
        {
            string[] known = [.. List().Select(session => $"'{session.Id}'")];
            throw new ArgumentException(
                $"No session is saved under '{id}' in '{Directory}'. " + (known.Length == 0 ? "The folder holds no session." : $"The sessions are: {string.Join(", ", known)}."), nameof(id));
        }

        SavedSession saved = Read(path, out JsonElement serialized);
        if (!saved.IsReadable)
        {
            throw new InvalidOperationException($"The saved session '{id}' cannot be read: {saved.Problem} The file '{path}' was left where it is; start a new session, or repair the file.");
        }

        AgentSession session;
        try
        {
            session = await agent.DeserializeSessionAsync(serialized, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            throw new InvalidOperationException(
                $"The agent cannot read the saved session '{id}': {ex.Message} The file '{path}' was left where it is; restore it with the agent configuration that saved it, or start a new session.", ex);
        }

        string[] tools = ToolNames(agent);
        return new RestoredSession(
            session,
            saved,
            [.. saved.Tools.Except(tools, StringComparer.OrdinalIgnoreCase)],
            [.. tools.Except(saved.Tools, StringComparer.OrdinalIgnoreCase)]);
    }

    private string PathOf(string id)
    {
        if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id is "." or "..")
        {
            throw new ArgumentException($"'{id}' is not a session id; take one from {nameof(List)}().", nameof(id));
        }
        return Path.Combine(Directory, $"{id}.json");
    }

    // One file as the list shows it; a file that cannot be read gets its name as id, the file's own time and the reason.
    private static SavedSession Read(string path, out JsonElement session)
    {
        session = default;
        string id = Path.GetFileNameWithoutExtension(path);
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("firstPrompt", out JsonElement firstPrompt) || firstPrompt.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("savedAt", out JsonElement savedAt) || !savedAt.TryGetDateTimeOffset(out DateTimeOffset at)
                || !root.TryGetProperty("tools", out JsonElement tools) || tools.ValueKind != JsonValueKind.Array
                || !root.TryGetProperty("session", out JsonElement saved))
            {
                return Unreadable(id, path, "It is JSON, but not a session this store wrote.");
            }

            session = saved.Clone();
            return new SavedSession(id, firstPrompt.GetString()!, at, [.. tools.EnumerateArray().Select(tool => tool.GetString() ?? string.Empty)], path);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return Unreadable(id, path, ex is JsonException ? "It is not valid JSON." : ex.Message);
        }
    }

    private static SavedSession Unreadable(string id, string path, string problem) =>
        new(id, string.Empty, File.GetLastWriteTimeUtc(path), [], path) { Problem = problem };

    // The tools the agent offers by default, which a ChatClientAgent hands out with its chat options; another kind of agent has none the store can see.
    private static string[] ToolNames(AIAgent agent) =>
        [.. agent.GetService<ChatOptions>()?.Tools?.Select(tool => tool.Name) ?? []];
}
