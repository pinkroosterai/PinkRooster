using System.Text;

namespace PinkRooster.ToolCollections.BuiltIn.FileOperations;

/// <summary>What <see cref="FileOperationsToolCollection.UndoAsync"/> did: the files it put back and the files it left as they are.</summary>
/// <param name="Restored">The files that are again as they were before the user message.</param>
/// <param name="Skipped">The files left as they are, each with the reason: changed by someone else since, or its copy is gone.</param>
public sealed record FileUndoResult(IReadOnlyList<FileUndoEntry> Restored, IReadOnlyList<FileUndoEntry> Skipped)
{
    /// <summary>True when the session held no change to take back, so nothing was touched.</summary>
    public bool NothingToUndo => Restored.Count == 0 && Skipped.Count == 0;

    /// <summary>
    /// The result as text, for the user and for the model: show it, and put it in front of the next prompt, because the conversation
    /// is not rewound and the model would go on believing its changes are there.
    /// </summary>
    public override string ToString()
    {
        if (NothingToUndo)
        {
            return "There is nothing to undo: no file change of this conversation is on record.";
        }

        StringBuilder text = new("Undo took back the file changes made for one user message. These files are now as listed, whatever the conversation above says.");
        Append(text, "Restored to what they were before that message:", Restored);
        Append(text, "Left as they are:", Skipped);
        return text.ToString();
    }

    private static void Append(StringBuilder text, string heading, IReadOnlyList<FileUndoEntry> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }
        text.Append('\n').Append(heading);
        foreach (FileUndoEntry entry in entries)
        {
            text.Append("\n- ").Append(entry.Path).Append(": ").Append(entry.Detail);
        }
    }
}
