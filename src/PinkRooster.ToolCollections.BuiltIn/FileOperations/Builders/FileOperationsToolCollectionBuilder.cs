using PinkRooster.ToolCollections.BuiltIn.Shared;

namespace PinkRooster.ToolCollections.BuiltIn.FileOperations;

/// <summary>Fluent builder for a <see cref="FileOperationsToolCollection"/>: the workspace, the read limit and backups, then <see cref="Build"/>.</summary>
/// <remarks>The builder is mutable: every method changes it and returns it. The workspace is required.</remarks>
/// <example>
/// <code>
/// FileOperationsToolCollection files = new FileOperationsToolCollectionBuilder()
///     .InWorkspace(workspace)
///     .KeepBackups()
///     .Build();
/// </code>
/// </example>
public sealed class FileOperationsToolCollectionBuilder
{
    private Workspace? workspace;
    private int maxReadCharacters = FileReadToolCollection.DefaultMaxReadCharacters;
    private bool keepBackups;

    /// <summary>Sets the workspace whose files the tools may reach and change.</summary>
    public FileOperationsToolCollectionBuilder InWorkspace(Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        this.workspace = workspace;
        return this;
    }

    /// <summary>Sets the most characters one read returns. Without it the limit is <see cref="FileReadToolCollection.DefaultMaxReadCharacters"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxReadCharacters"/> is below 1.</exception>
    public FileOperationsToolCollectionBuilder WithMaxReadCharacters(int maxReadCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxReadCharacters, 1);
        this.maxReadCharacters = maxReadCharacters;
        return this;
    }

    /// <summary>
    /// Keeps a copy of a file's previous content in <c>.pinkrooster/backups</c> before it is edited, overwritten or deleted, and records
    /// every change per session, so <see cref="FileOperationsToolCollection.UndoAsync"/> can take a user message's changes back.
    /// </summary>
    public FileOperationsToolCollectionBuilder KeepBackups()
    {
        keepBackups = true;
        return this;
    }

    /// <summary>Builds a new collection from the builder's current settings. Can be called more than once; each collection is independent.</summary>
    /// <exception cref="InvalidOperationException">No workspace was set; call <see cref="InWorkspace"/>.</exception>
    public FileOperationsToolCollection Build() =>
        new(workspace ?? throw new InvalidOperationException("A file collection needs a workspace; call InWorkspace(new Workspace(rootDirectory)) before Build()."), maxReadCharacters, keepBackups);
}
