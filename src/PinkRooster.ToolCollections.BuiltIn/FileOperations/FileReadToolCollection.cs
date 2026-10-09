using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileSystemGlobbing;
using PinkRooster.ToolCollections.BuiltIn.Shared;

namespace PinkRooster.ToolCollections.BuiltIn.FileOperations;

/// <summary>
/// Gives an agent read-only access to the files of a workspace: finding, searching, listing and reading, sandboxed to the
/// workspace's root. Use it alone for an agent that must not change files; <see cref="FileOperationsToolCollection" /> adds the tools that do.
/// </summary>
public class FileReadToolCollection : ToolCollection
{
    /// <summary>
    /// The default maximum reply size in characters for reading files, listing directories and searching (51,200).
    /// </summary>
    public const int DefaultMaxReadCharacters = 51200;

    // A longer line is cut: a minified file would otherwise fill a whole reply with one line.
    private const int MaxLineCharacters = 2000;

    // How long one search pattern may take on one line before the search stops.
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    // The most files FindFiles lists.
    private const int MaxFoundFiles = 100;

    private protected readonly Workspace workspace;
    private protected readonly string baseDirectory;
    private readonly IReadOnlyList<string> skippedDirectories;
    private readonly int maxReadCharacters;

    /// <summary>
    /// Creates a new <see cref="FileReadToolCollection"/> sandboxed to the workspace's root directory, with the default read limit.
    /// </summary>
    /// <remarks>For another read limit use <see cref="FileReadToolCollectionBuilder"/>.</remarks>
    /// <param name="workspace">The workspace whose root directory all operations are sandboxed within; share it with the shell collection.</param>
    public FileReadToolCollection(Workspace workspace) : this(workspace, DefaultMaxReadCharacters)
    {
    }

    /// <param name="workspace">The workspace whose root directory all operations are sandboxed within.</param>
    /// <param name="maxReadCharacters">The most characters a read, a listing or a search returns in one reply.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxReadCharacters"/> is less than or equal to zero.</exception>
    internal FileReadToolCollection(Workspace workspace, int maxReadCharacters)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        this.workspace = workspace;
        baseDirectory = workspace.RootDirectory;
        skippedDirectories = workspace.SkippedDirectories;

        if (maxReadCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxReadCharacters), "Max read characters must be greater than zero.");
        }

        this.maxReadCharacters = maxReadCharacters;

        AddInstruction($"File operations are sandboxed to the base directory: {this.baseDirectory}. Always pass paths relative to this directory.");
        AddInstruction($"A read, a listing or a search returns at most {this.maxReadCharacters} characters per call; a reply that is cut says how to continue.");
    }

    /// <summary>
    /// Gets the canonical base directory all operations stay inside.
    /// </summary>
    public string BaseDirectory => baseDirectory;

    /// <summary>
    /// Gets the most characters a single read, listing or search returns.
    /// </summary>
    public int MaxReadCharacters => maxReadCharacters;

    /// <summary>
    /// Lists the entries directly inside one directory.
    /// </summary>
    [Tool("ListDirectory",
        "Lists the entries directly inside one directory: sub-directories first as 'name/', then files as 'name (size in bytes)'. " +
        "Use it to see what a folder holds; it does not recurse (to find files below a folder use FindFiles).",
        Kind = ToolKind.Read)]
    public Task<string> ListDirectory(
        [Description("The directory to list. Leave empty for the base directory itself.")]
        string? path = null,
        CancellationToken cancellationToken = default)
    {
        return ToolErrors.RunOnPoolAsync(() =>
        {
            string lookupPath = string.IsNullOrWhiteSpace(path) ? "." : path;
            if (!PathGuard.TryResolvePath(baseDirectory, lookupPath, out string canonicalPath, out string? pathError))
            {
                return pathError!;
            }

            if (!Directory.Exists(canonicalPath))
            {
                if (File.Exists(canonicalPath))
                {
                    return $"Error: '{lookupPath}' is a file, not a directory. Use ReadFile to view file contents.";
                }

                return $"Error: Directory not found: '{lookupPath}'.";
            }

            List<FileSystemInfo> entries = new DirectoryInfo(canonicalPath).EnumerateFileSystemInfos()
                .OrderBy(e => e is FileInfo ? 1 : 0)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (entries.Count == 0)
            {
                return "Directory is empty.";
            }

            StringBuilder sb = new StringBuilder();
            int characters = 0;
            int shown = 0;
            foreach (FileSystemInfo entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string line = entry is FileInfo file ? $"{entry.Name} ({file.Length} bytes)" : $"{entry.Name}/";
                int lineLength = line.Length + (shown == 0 ? 0 : 1);
                if (characters + lineLength > maxReadCharacters && shown > 0)
                {
                    break;
                }

                if (shown > 0)
                {
                    sb.Append('\n');
                }

                sb.Append(line);
                characters += lineLength;
                shown++;
            }

            if (shown < entries.Count)
            {
                sb.Append($"\n[... {entries.Count - shown} more entries not shown; the listing is cut at the read limit of {maxReadCharacters} characters]");
            }

            return sb.ToString();
        }, cancellationToken);
    }

    /// <summary>
    /// Reads text file content within the base directory with optional line ranges.
    /// </summary>
    [Tool("ReadFile",
        "Reads a text file and returns its lines, each prefixed with its line number as 'N: text'. " +
        "Pass startLine and endLine to read one section. The reply says whether more lines follow and which startLine continues; " +
        "binary and non-UTF-8 files are refused. The 'N: ' prefixes are not part of the file, so leave them out when you pass text to EditFile.",
        Kind = ToolKind.Read)]
    public Task<string> ReadFile(
        [Description("The file to read.")]
        string path,
        [Description("Optional 1-indexed line to start at. Leave empty to start at line 1.")]
        int? startLine = null,
        [Description("Optional 1-indexed line to stop at, inclusive. Leave empty to read to the end, or as far as one reply holds.")]
        int? endLine = null,
        CancellationToken cancellationToken = default)
    {
        return ToolErrors.RunAsync(async () =>
        {
            if (!TryResolveExistingFile(path, ifMissing: null, out string canonicalPath, out string? pathError))
            {
                return pathError!;
            }

            int start = startLine ?? 1;
            if (start < 1)
            {
                return $"Error: startLine must be at least 1, but got {start}.";
            }

            if (endLine.HasValue && endLine.Value < start)
            {
                return $"Error: endLine ({endLine.Value}) cannot be less than startLine ({start}).";
            }

            string notText = $"Error: File '{path}' contains binary or non-UTF-8 content and cannot be read.";
            if (TextFile.StartsWithBinaryContent(canonicalPath))
            {
                return notText;
            }

            // Streams line by line and stops at the limit, so a large file is never held in memory.
            StringBuilder page = new StringBuilder();
            int currentLine = 0;
            int shownLines = 0;
            int pageCharacters = 0;
            bool moreLines = false;

            try
            {
                using StreamReader reader = new StreamReader(canonicalPath, TextFile.StrictUtf8, detectEncodingFromByteOrderMarks: false);
                string? line;
                while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
                {
                    if (line.Contains('\0'))
                    {
                        return notText;
                    }

                    currentLine++;
                    if (currentLine < start)
                    {
                        continue;
                    }

                    if (endLine.HasValue && currentLine > endLine.Value)
                    {
                        moreLines = true;
                        break;
                    }

                    if (line.Length > MaxLineCharacters)
                    {
                        line = $"{line[..MaxLineCharacters]} [... {line.Length - MaxLineCharacters} more characters]";
                    }

                    string numbered = $"{currentLine}: {line}";
                    int length = numbered.Length + (shownLines == 0 ? 0 : 1);
                    if (pageCharacters + length > maxReadCharacters)
                    {
                        if (shownLines > 0)
                        {
                            moreLines = true;
                            break;
                        }

                        // Not even one line fits: the first line is cut to the limit
                        numbered = OutputBudget.CutToLength(numbered, maxReadCharacters);
                        length = numbered.Length;
                    }

                    if (shownLines > 0)
                    {
                        page.Append('\n');
                    }

                    page.Append(numbered);
                    pageCharacters += length;
                    shownLines++;
                }
            }
            catch (DecoderFallbackException)
            {
                return notText;
            }

            if (currentLine == 0)
            {
                return "File is empty (0 lines).";
            }

            if (shownLines == 0)
            {
                return $"Error: startLine ({start}) is beyond the end of file '{path}' (file has {currentLine} lines). Valid line range is 1-{currentLine}.";
            }

            int lastShown = start + shownLines - 1;
            string status = moreLines
                ? $"more lines follow; continue with startLine={lastShown + 1}"
                : $"of {currentLine}, end of file";
            return $"Lines {start}-{lastShown} ({status}):\n{page}";
        });
    }

    /// <summary>
    /// Finds files by name with a glob pattern, newest first.
    /// </summary>
    [Tool("FindFiles",
        "Finds files by name with a glob pattern and returns their paths, newest first, at most 100. " +
        "Use it to locate a file when you know part of its name; to find files by what they contain use SearchFiles, and to see one folder use ListDirectory. " +
        "'*' matches within a name, '**/' crosses folders, and a pattern with no '/' matches file names at any depth ('*.cs'). " +
        "Version-control and build folders (.git, node_modules, bin, obj by default) are skipped.",
        Kind = ToolKind.Read)]
    public Task<string> FindFiles(
        [Description("The glob pattern, such as '*.cs', 'src/**/*Tests.cs' or 'docs/*.md', matched against paths below the starting folder.")]
        string pattern,
        [Description("The folder to search below. Leave empty for the base directory.")]
        string? path = null,
        CancellationToken cancellationToken = default)
    {
        return ToolErrors.RunOnPoolAsync(() =>
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                return "Error: pattern cannot be empty. Pass a glob such as '*.cs' or 'src/**/*.json'.";
            }

            if (!TryResolveDirectory(path, out string directory, out string? pathError))
            {
                return pathError!;
            }

            Matcher matcher = FileSearcher.CreateMatcher(pattern);
            List<FileInfo> found = FileSearcher.EnumerateFiles(directory, skippedDirectories, cancellationToken)
                .Where(file => FileSearcher.IsMatch(matcher, FileSearcher.RelativePath(directory, file.FullName)))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenBy(file => file.FullName, StringComparer.Ordinal)
                .ToList();

            if (found.Count == 0)
            {
                return $"No files match '{pattern}'.";
            }

            string listing = string.Join("\n", found.Take(MaxFoundFiles).Select(file => FileSearcher.RelativePath(baseDirectory, file.FullName)));
            return found.Count > MaxFoundFiles
                ? $"{listing}\n[... {found.Count - MaxFoundFiles} more files match; narrow the pattern or the path]"
                : listing;
        }, cancellationToken);
    }

    /// <summary>
    /// Searches the text of files for a regular expression.
    /// </summary>
    [Tool("SearchFiles",
        "Searches the text of files for a regular expression (.NET syntax) and returns where it matches. " +
        "Use it to find where something is defined or used before reading files; to find files by name use FindFiles. " +
        "mode picks the answer: files, matching lines or counts. Binary files and the folders FindFiles skips are left out.",
        Kind = ToolKind.Read)]
    public Task<string> SearchFiles(
        [Description("The regular expression, matched against one line at a time. Escape regex characters you mean literally, for example 'Run\\(' for 'Run('.")]
        string pattern,
        [Description("The file or folder to search. Leave empty to search the whole base directory.")]
        string? path = null,
        [Description("Optional glob that limits the search to matching files, such as '*.cs' or 'src/**/*.json'.")]
        string? include = null,
        [Description("What to return: Files (paths, the default), Lines (matching lines as 'path:line: text') or Counts (matching lines per file).")]
        SearchMode mode = SearchMode.Files,
        [Description("True to ignore case when matching.")]
        bool ignoreCase = false,
        [Description("The most files (Files, Counts) or lines (Lines) to report.")]
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        return ToolErrors.RunOnPoolAsync(() =>
        {
            if (string.IsNullOrEmpty(pattern))
            {
                return "Error: pattern cannot be empty.";
            }

            if (limit < 1)
            {
                return $"Error: limit must be at least 1, but got {limit}.";
            }

            Regex regex;
            try
            {
                regex = new Regex(pattern, RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None), RegexTimeout);
            }
            catch (ArgumentException ex)
            {
                return $"Error: pattern is not a valid regular expression: {ex.Message}";
            }

            if (!PathGuard.TryResolvePath(baseDirectory, string.IsNullOrWhiteSpace(path) ? "." : path, out string canonicalPath, out string? pathError))
            {
                return pathError!;
            }

            IEnumerable<(string Display, FileInfo File)> files;
            if (File.Exists(canonicalPath))
            {
                files = [(FileSearcher.RelativePath(baseDirectory, canonicalPath), new FileInfo(canonicalPath))];
            }
            else if (Directory.Exists(canonicalPath))
            {
                Matcher? includeMatcher = string.IsNullOrWhiteSpace(include) ? null : FileSearcher.CreateMatcher(include);
                files = FileSearcher.EnumerateFiles(canonicalPath, skippedDirectories, cancellationToken)
                    .Where(file => includeMatcher is null || FileSearcher.IsMatch(includeMatcher, FileSearcher.RelativePath(canonicalPath, file.FullName)))
                    .Select(file => (FileSearcher.RelativePath(baseDirectory, file.FullName), file));
            }
            else
            {
                return $"Error: Path not found: '{path}'. Use ListDirectory or FindFiles to see what exists.";
            }

            try
            {
                return FileSearcher.Search(files, regex, mode, limit, maxReadCharacters, cancellationToken);
            }
            catch (RegexMatchTimeoutException)
            {
                return $"Error: the search took longer than {RegexTimeout.TotalSeconds:0} s on one line. Simplify the pattern, or narrow the search with path or include.";
            }
        }, cancellationToken);
    }

    // Resolves an optional path that must name an existing directory; empty means the base directory.
    private bool TryResolveDirectory(string? path, out string directory, out string? error)
    {
        string lookup = string.IsNullOrWhiteSpace(path) ? "." : path;
        if (!PathGuard.TryResolvePath(baseDirectory, lookup, out directory, out error))
        {
            return false;
        }

        if (Directory.Exists(directory))
        {
            return true;
        }

        error = File.Exists(directory)
            ? $"Error: '{lookup}' is a file, not a directory. Use ReadFile to view it or SearchFiles to search it."
            : $"Error: Directory not found: '{lookup}'.";
        return false;
    }

    // Resolves a path that must name an existing file; the error names the tool to use next.
    private protected bool TryResolveExistingFile(string path, string? ifMissing, out string canonicalPath, out string? error, bool followFinalLink = true)
    {
        if (!PathGuard.TryResolvePath(baseDirectory, path, out canonicalPath, out error, followFinalLink))
        {
            return false;
        }

        if (File.Exists(canonicalPath))
        {
            return true;
        }

        error = Directory.Exists(canonicalPath)
            ? $"Error: '{path}' is a directory, not a file. Use ListDirectory to view its entries."
            : $"Error: File not found: '{path}'. {ifMissing ?? "Use ListDirectory on the parent directory to inspect existing files."}";
        return false;
    }
}
