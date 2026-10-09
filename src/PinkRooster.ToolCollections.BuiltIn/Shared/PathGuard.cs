using System.Runtime.InteropServices;

namespace PinkRooster.ToolCollections.BuiltIn.Shared;

/// <summary>
/// Enforces path sandboxing and canonicalization so operations stay inside the Base Directory.
/// </summary>
/// <remarks>
/// The check and the file operation that follows it are two steps: a link swapped in between them is not caught, and a
/// hard link to a file outside the Base Directory is followed.
/// </remarks>
internal static class PathGuard
{
    // A chain of links longer than this is treated as a loop, as the operating system does.
    private const int MaxLinkHops = 40;

    private static readonly bool IsCaseInsensitiveOS =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    /// <summary>
    /// Validates that the base directory exists and resolves it to a canonical, normalized absolute path.
    /// </summary>
    /// <param name="baseDirectory">The host-supplied base directory path.</param>
    /// <returns>
    /// The absolute base directory path with every symbolic link in it resolved and trailing directory separators trimmed.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when the path is null, whitespace, or does not exist.</exception>
    public static string CanonicalizeBaseDirectory(string? baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            throw new ArgumentException("Base directory cannot be null or whitespace.", nameof(baseDirectory));
        }

        if (!Directory.Exists(baseDirectory))
        {
            throw new ArgumentException($"Base directory does not exist: '{baseDirectory}'", nameof(baseDirectory));
        }

        try
        {
            return Path.TrimEndingDirectorySeparator(ResolveLinks(Path.GetFullPath(baseDirectory)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ArgumentException($"Failed to resolve base directory link target: {ex.Message}", nameof(baseDirectory), ex);
        }
    }

    /// <summary>
    /// Validates a requested path, resolving relative navigation and symbolic links, verifying it stays within the base directory.
    /// </summary>
    /// <param name="canonicalBaseDirectory">The canonical base directory.</param>
    /// <param name="requestedPath">The path requested by the model or caller.</param>
    /// <param name="canonicalPath">The resolved canonical path if valid; otherwise empty.</param>
    /// <param name="errorMessage">The error message starting with 'Error:' if invalid; otherwise null.</param>
    /// <param name="followFinalLink">
    /// <c>false</c> to resolve the links above the last part of the path only, for an operation that acts on the entry
    /// itself (delete, move) and must not reach through a link to its target.
    /// </param>
    /// <returns><c>true</c> if valid and within sandbox; otherwise <c>false</c>.</returns>
    public static bool TryResolvePath(
        string canonicalBaseDirectory,
        string? requestedPath,
        out string canonicalPath,
        out string? errorMessage,
        bool followFinalLink = true)
    {
        canonicalPath = string.Empty;

        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            errorMessage = "Error: Path cannot be empty or whitespace. Pass a path relative to the base directory.";
            return false;
        }

        string absolutePath;
        try
        {
            absolutePath = Path.TrimEndingDirectorySeparator(Path.IsPathRooted(requestedPath)
                ? Path.GetFullPath(requestedPath)
                : Path.GetFullPath(Path.Combine(canonicalBaseDirectory, requestedPath)));
        }
        catch (Exception ex)
        {
            errorMessage = $"Error: Invalid path '{requestedPath}': {ex.Message}";
            return false;
        }

        // Fast boundary check on the normalized path, before the filesystem is touched
        if (!IsInsideOrEqual(absolutePath, canonicalBaseDirectory))
        {
            errorMessage = $"Error: Path '{requestedPath}' must stay inside the base directory. Pass a relative path inside it.";
            return false;
        }

        // Check if path is the base directory itself
        if (Path.GetRelativePath(canonicalBaseDirectory, absolutePath) == ".")
        {
            canonicalPath = canonicalBaseDirectory;
            errorMessage = null;
            return true;
        }

        string resolved;
        try
        {
            resolved = followFinalLink
                ? ResolveLinks(absolutePath)
                : Path.Combine(ResolveLinks(Path.GetDirectoryName(absolutePath)!), Path.GetFileName(absolutePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errorMessage = $"Error: Unable to resolve symbolic link for '{requestedPath}': {ex.Message}";
            return false;
        }

        // The normalized path was inside, so only a link can have taken the resolved one out
        if (!IsInsideOrEqual(resolved, canonicalBaseDirectory))
        {
            errorMessage = $"Error: Path '{requestedPath}' traverses outside the base directory via symbolic link.";
            return false;
        }

        canonicalPath = resolved;
        errorMessage = null;
        return true;
    }

    /// <summary>
    /// Resolves every symbolic link in an absolute path, as the operating system would when opening it. A link's target is
    /// walked part by part in turn, so a link that leads through another link is followed to the end. Parts that do not
    /// exist are kept as written.
    /// </summary>
    /// <exception cref="IOException">Thrown when the links form a loop.</exception>
    private static string ResolveLinks(string absolutePath)
    {
        string current = Path.GetPathRoot(absolutePath) ?? string.Empty;
        Stack<string> pending = new Stack<string>();
        PushSegments(pending, absolutePath[current.Length..]);
        int hops = 0;

        while (pending.TryPop(out string? segment))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                // current holds no links any more, so its parent is the directory the operating system would reach
                current = Path.GetDirectoryName(current) ?? current;
                continue;
            }

            string next = Path.Combine(current, segment);
            string? linkTarget = LinkTargetOf(next);
            if (linkTarget is null)
            {
                current = next;
                continue;
            }

            if (++hops > MaxLinkHops)
            {
                throw new IOException("Too many levels of symbolic links.");
            }

            if (Path.IsPathRooted(linkTarget))
            {
                // On Windows a rooted target can still lack a drive ('\dir'); it takes the drive of the link's directory
                if (!Path.IsPathFullyQualified(linkTarget))
                {
                    linkTarget = Path.GetFullPath(linkTarget, current);
                }

                current = Path.GetPathRoot(linkTarget)!;
                PushSegments(pending, linkTarget[current.Length..]);
            }
            else
            {
                PushSegments(pending, linkTarget);
            }
        }

        return current;
    }

    private static string? LinkTargetOf(string path)
    {
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        return info.LinkTarget;
    }

    private static void PushSegments(Stack<string> pending, string path)
    {
        string[] segments = path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        for (int i = segments.Length - 1; i >= 0; i--)
        {
            pending.Push(segments[i]);
        }
    }

    /// <summary>
    /// Checks if a target path is the base directory or a subdirectory/file within it, respecting directory boundaries.
    /// </summary>
    private static bool IsInsideOrEqual(string targetPath, string basePath)
    {
        string normalizedBase = Path.TrimEndingDirectorySeparator(Path.GetFullPath(basePath));
        string normalizedTarget = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetPath));

        StringComparison comparison = IsCaseInsensitiveOS
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (normalizedTarget.Equals(normalizedBase, comparison))
        {
            return true;
        }

        string baseWithSep = normalizedBase + Path.DirectorySeparatorChar;
        return normalizedTarget.StartsWith(baseWithSep, comparison);
    }
}
