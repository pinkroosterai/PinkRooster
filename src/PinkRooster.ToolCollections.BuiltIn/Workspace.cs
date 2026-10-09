using PinkRooster.ToolCollections.BuiltIn.Shared;

namespace PinkRooster.ToolCollections.BuiltIn;

/// <summary>
/// The folder the built-in file and shell tools work in. Create one and pass it to every collection that should share the
/// folder, so the file tools and the shell agree on what the root is and which folders a search leaves out.
/// </summary>
public sealed class Workspace
{
    /// <summary>Folders the search tools leave out by default: version control data, build output and this library's own <c>.pinkrooster</c> folder.</summary>
    public static IReadOnlyList<string> DefaultSkippedDirectories { get; } = [".git", "node_modules", "bin", "obj", ".pinkrooster"];

    /// <summary>Creates a workspace rooted at an existing folder.</summary>
    /// <param name="rootDirectory">The root. It must exist; symbolic links in it are resolved.</param>
    /// <param name="skippedDirectories">
    /// Folder names the search tools do not enter, at any depth. Defaults to <see cref="DefaultSkippedDirectories" />.
    /// A search that names such a folder as its starting point still searches it.
    /// </param>
    /// <exception cref="ArgumentException">The root is blank or does not exist.</exception>
    public Workspace(string rootDirectory, IReadOnlyList<string>? skippedDirectories = null)
    {
        RootDirectory = PathGuard.CanonicalizeBaseDirectory(rootDirectory);
        SkippedDirectories = skippedDirectories ?? DefaultSkippedDirectories;
    }

    /// <summary>The canonical root: absolute, with links resolved and no trailing separator.</summary>
    public string RootDirectory { get; }

    /// <summary>The folder names the search tools do not enter.</summary>
    public IReadOnlyList<string> SkippedDirectories { get; }
}
