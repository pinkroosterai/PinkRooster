using PinkRooster.ToolCollections.BuiltIn.Shared;

namespace PinkRooster.ToolCollections.BuiltIn.Shells;

/// <summary>Fluent builder for a <see cref="ShellToolCollection"/>: the workspace, limits, allow and deny lists and shell, then <see cref="Build"/>.</summary>
/// <remarks>
/// The builder is mutable: every method changes it and returns it. Everything is optional; what is left out keeps its
/// default. Bad values fail at the call, and a deny list without an allow list fails at <see cref="Build"/>.
/// </remarks>
/// <example>
/// <code>
/// ShellToolCollection shell = new ShellToolCollectionBuilder()
///     .InWorkspace(workspace)
///     .WithTimeout(TimeSpan.FromSeconds(30))
///     .AllowPrefixes("git", "dotnet")
///     .DenyPrefixes("git push")
///     .Build();
/// </code>
/// </example>
public sealed class ShellToolCollectionBuilder
{
    private Workspace? workspace;
    private TimeSpan? timeout;
    private TimeSpan? maxTimeout;
    private int? maxOutputCharacters;
    private string[]? allowlistPrefixes;
    private string[]? denylistPrefixes;
    private Shell? shell;
    private Dictionary<string, string?>? environment;
    private bool separateStreams;
    private bool inheritEnvironment = true;

    /// <summary>Sets the workspace commands start in and may not leave. Without it the current directory is used.</summary>
    public ShellToolCollectionBuilder InWorkspace(Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        this.workspace = workspace;
        return this;
    }

    /// <summary>Sets how long a command runs before it is stopped. Without it the limit is 120 seconds.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is zero or negative.</exception>
    public ShellToolCollectionBuilder WithTimeout(TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        this.timeout = timeout;
        return this;
    }

    /// <summary>Sets the longest timeout the model may ask for. It must not be shorter than <see cref="WithTimeout"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxTimeout"/> is zero or negative.</exception>
    public ShellToolCollectionBuilder WithMaxTimeout(TimeSpan maxTimeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxTimeout, TimeSpan.Zero);
        this.maxTimeout = maxTimeout;
        return this;
    }

    /// <summary>Sets the most characters one reply may hold. Without it the limit is 30,000.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxOutputCharacters"/> is below 1.</exception>
    public ShellToolCollectionBuilder WithMaxOutputCharacters(int maxOutputCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxOutputCharacters, 1);
        this.maxOutputCharacters = maxOutputCharacters;
        return this;
    }

    /// <summary>Limits the shell to commands that start with one of these prefixes, one command per call.</summary>
    public ShellToolCollectionBuilder AllowPrefixes(params string[] prefixes)
    {
        ArgumentNullException.ThrowIfNull(prefixes);
        allowlistPrefixes = [.. prefixes];
        return this;
    }

    /// <summary>Refuses commands that start with one of these prefixes. It carves exceptions out of <see cref="AllowPrefixes"/> and fails at <see cref="Build"/> without it.</summary>
    public ShellToolCollectionBuilder DenyPrefixes(params string[] prefixes)
    {
        ArgumentNullException.ThrowIfNull(prefixes);
        denylistPrefixes = [.. prefixes];
        return this;
    }

    /// <summary>Sets the shell commands run in. Without it the platform's default shell is used.</summary>
    public ShellToolCollectionBuilder UseShell(Shell shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        this.shell = shell;
        return this;
    }

    /// <summary>Sets an environment variable for every command; a null value removes it. Can be called more than once.</summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank or contains '='.</exception>
    public ShellToolCollectionBuilder WithEnvironmentVariable(string name, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Contains('='))
        {
            throw new ArgumentException($"Environment variable name '{name}' must not contain '='.", nameof(name));
        }

        (environment ??= [])[name] = value;
        return this;
    }

    /// <summary>Returns standard output and standard error as separate parts of the reply.</summary>
    public ShellToolCollectionBuilder SeparateStreams()
    {
        separateStreams = true;
        return this;
    }

    /// <summary>Starts commands without the host's environment variables, only the ones from <see cref="WithEnvironmentVariable"/>.</summary>
    public ShellToolCollectionBuilder WithoutInheritedEnvironment()
    {
        inheritEnvironment = false;
        return this;
    }

    /// <summary>Builds a new collection from the builder's current settings. Can be called more than once; each collection is independent.</summary>
    /// <exception cref="ArgumentException">A deny list was set without an allow list.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The maximum timeout is shorter than the timeout, or the output limit is below the minimum.</exception>
    public ShellToolCollection Build() => new(
        workspace,
        timeout,
        maxOutputCharacters,
        allowlistPrefixes is null ? null : [.. allowlistPrefixes],
        denylistPrefixes is null ? null : [.. denylistPrefixes],
        shell,
        maxTimeout,
        environment is null ? null : new Dictionary<string, string?>(environment),
        separateStreams,
        inheritEnvironment);
}
