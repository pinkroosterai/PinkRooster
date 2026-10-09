using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileSystemGlobbing;

namespace PinkRooster.ToolCollections.BuiltIn.FileOperations;

/// <summary>Walks a folder for files and searches their text. Linked entries are never followed, so a search cannot leave the workspace through a link.</summary>
internal static class FileSearcher
{
    /// <summary>Files larger than this are not searched; the reply counts them.</summary>
    public const long MaxSearchFileBytes = 10 * 1024 * 1024;

    private const int MaxMatchedLineCharacters = 200;

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>A glob over paths relative to the folder searched. A pattern with no <c>/</c> matches file names at any depth.</summary>
    public static Matcher CreateMatcher(string pattern)
    {
        Matcher matcher = new Matcher(PathComparison);
        string normalized = pattern.Replace('\\', '/');
        matcher.AddInclude(normalized.Contains('/') ? normalized : "**/" + normalized);
        return matcher;
    }

    /// <summary>Whether a path relative to the searched folder matches the glob.</summary>
    public static bool IsMatch(Matcher matcher, string relativePath) => matcher.Match([relativePath]).HasMatches;

    /// <summary>
    /// The files below a folder, each folder's files before its subfolders, both in name order, leaving out the named folders
    /// and every symbolic link. Folders that cannot be read are left out.
    /// </summary>
    public static IEnumerable<FileInfo> EnumerateFiles(string directory, IReadOnlyList<string> skippedDirectories, CancellationToken cancellationToken)
    {
        Stack<string> pending = new Stack<string>();
        pending.Push(directory);
        EnumerationOptions options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System };
        while (pending.TryPop(out string? current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<FileSystemInfo> entries = new DirectoryInfo(current).EnumerateFileSystemInfos("*", options)
                .Where(entry => entry.LinkTarget is null)
                .OrderBy(entry => entry.Name, StringComparer.Ordinal)
                .ToList();

            foreach (FileInfo file in entries.OfType<FileInfo>())
            {
                yield return file;
            }

            // Pushed in reverse, so the first subfolder is walked first
            foreach (DirectoryInfo? subdirectory in entries.OfType<DirectoryInfo>().Reverse())
            {
                if (!skippedDirectories.Any(name => string.Equals(name, subdirectory.Name, PathComparison)))
                {
                    pending.Push(subdirectory.FullName);
                }
            }
        }
    }

    /// <summary>The path of a file relative to a folder, with <c>/</c> separators.</summary>
    public static string RelativePath(string directory, string file) =>
        Path.GetRelativePath(directory, file).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>
    /// Searches the files' text line by line and formats the answer. Binary files, files that are not UTF-8 and files over
    /// <see cref="MaxSearchFileBytes" /> are counted, not searched.
    /// </summary>
    /// <param name="files">The files to search, as the path to show for each and the file.</param>
    /// <param name="regex">The pattern; a <see cref="RegexMatchTimeoutException" /> from it is the caller's to handle.</param>
    /// <param name="mode">What to answer with.</param>
    /// <param name="limit">The most files (<see cref="SearchMode.Files" />, <see cref="SearchMode.Counts" />) or lines (<see cref="SearchMode.Lines" />) to report.</param>
    /// <param name="maxCharacters">The most characters the answer may hold.</param>
    /// <param name="cancellationToken">Stops the search.</param>
    public static string Search(IEnumerable<(string Display, FileInfo File)> files, Regex regex, SearchMode mode, int limit, int maxCharacters, CancellationToken cancellationToken)
    {
        StringBuilder output = new StringBuilder();
        int characters = 0;
        int reported = 0;
        int skipped = 0;
        int totalMatches = 0;
        int matchedFiles = 0;
        bool cut = false;

        bool TryAppend(string line)
        {
            int lineLength = line.Length + (output.Length == 0 ? 0 : 1);
            if (characters + lineLength > maxCharacters)
            {
                return false;
            }

            if (output.Length > 0)
            {
                output.Append('\n');
            }

            output.Append(line);
            characters += lineLength;
            return true;
        }

        foreach ((string? display, FileInfo? file) in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryReadMatches(file, regex, mode == SearchMode.Files, out List<(int Line, string Text)> matches))
            {
                skipped++;
                continue;
            }

            if (matches.Count == 0)
            {
                continue;
            }

            matchedFiles++;
            totalMatches += matches.Count;

            if (mode == SearchMode.Lines)
            {
                foreach ((int lineNumber, string? text) in matches)
                {
                    if (reported >= limit || !TryAppend($"{display}:{lineNumber}: {Shorten(text)}"))
                    {
                        cut = true;
                        break;
                    }

                    reported++;
                }
            }
            else if (reported >= limit || !TryAppend(mode == SearchMode.Files ? display : $"{display}: {matches.Count}"))
            {
                cut = true;
            }
            else
            {
                reported++;
            }

            if (cut)
            {
                break;
            }
        }

        List<string> notices = new List<string>();
        if (cut)
        {
            notices.Add("[... more matches not shown; narrow the search with path or include]");
        }
        else if (mode == SearchMode.Counts && matchedFiles > 0)
        {
            notices.Add($"Total: {totalMatches} matching lines in {matchedFiles} files");
        }

        if (skipped > 0)
        {
            notices.Add($"[{skipped} files not searched: binary, not UTF-8, unreadable or over {MaxSearchFileBytes / (1024 * 1024)} MB]");
        }

        if (output.Length == 0)
        {
            output.Append($"No matches for '{regex}'.");
        }

        foreach (string notice in notices)
        {
            output.Append('\n').Append(notice);
        }

        return output.ToString();
    }

    // False when the file cannot be searched. In files mode the read stops at the first match.
    private static bool TryReadMatches(FileInfo file, Regex regex, bool firstOnly, out List<(int Line, string Text)> matches)
    {
        matches = [];
        try
        {
            if (file.Length > MaxSearchFileBytes || TextFile.StartsWithBinaryContent(file.FullName))
            {
                return false;
            }

            using StreamReader reader = new StreamReader(file.FullName, TextFile.StrictUtf8, detectEncodingFromByteOrderMarks: false);
            int lineNumber = 0;
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                lineNumber++;
                if (line.Contains('\0'))
                {
                    return false;
                }

                if (regex.IsMatch(line))
                {
                    matches.Add((lineNumber, line));
                    if (firstOnly)
                    {
                        return true;
                    }
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return false;
        }
    }

    private static string Shorten(string line)
    {
        string trimmed = line.Trim();
        return trimmed.Length > MaxMatchedLineCharacters ? trimmed[..MaxMatchedLineCharacters] + " [...]" : trimmed;
    }
}
