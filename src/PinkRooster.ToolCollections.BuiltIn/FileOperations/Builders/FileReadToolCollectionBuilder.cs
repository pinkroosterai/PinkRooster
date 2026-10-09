using PinkRooster.ToolCollections.BuiltIn.Shared;

namespace PinkRooster.ToolCollections.BuiltIn.FileOperations;

/// <summary>Fluent builder for a <see cref="FileReadToolCollection"/>: the workspace and the read limit, then <see cref="Build"/>.</summary>
/// <remarks>The builder is mutable: every method changes it and returns it. The workspace is required.</remarks>
/// <example>
/// <code>
/// FileReadToolCollection files = new FileReadToolCollectionBuilder()
///     .InWorkspace(workspace)
///     .WithMaxReadCharacters(20_000)
///     .Build();
/// </code>
/// </example>
public sealed class FileReadToolCollectionBuilder
{
    private Workspace? workspace;
    private int maxReadCharacters = FileReadToolCollection.DefaultMaxReadCharacters;

    /// <summary>Sets the workspace whose files the tools may reach.</summary>
    public FileReadToolCollectionBuilder InWorkspace(Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        this.workspace = workspace;
        return this;
    }

    /// <summary>Sets the most characters one read returns. Without it the limit is <see cref="FileReadToolCollection.DefaultMaxReadCharacters"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxReadCharacters"/> is below 1.</exception>
    public FileReadToolCollectionBuilder WithMaxReadCharacters(int maxReadCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxReadCharacters, 1);
        this.maxReadCharacters = maxReadCharacters;
        return this;
    }

    /// <summary>Builds a new collection from the builder's current settings. Can be called more than once; each collection is independent.</summary>
    /// <exception cref="InvalidOperationException">No workspace was set; call <see cref="InWorkspace"/>.</exception>
    public FileReadToolCollection Build() =>
        new(workspace ?? throw new InvalidOperationException("A file collection needs a workspace; call InWorkspace(new Workspace(rootDirectory)) before Build()."), maxReadCharacters);
}
