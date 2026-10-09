using System.Text.Json;
using Microsoft.Extensions.AI;

namespace PinkRooster.Agents.Permissions;

/// <summary>
/// A standing answer of a <see cref="PermissionPolicy"/>: the calls of one tool that run without asking. It covers every call of
/// the tool, the calls whose text argument starts with a prefix, or the calls whose text argument is exactly one text.
/// </summary>
public sealed record AllowRule
{
    // What ends one command and starts another, or sends output elsewhere, in the common shells. A prefix rule never matches text
    // that holds one, so an allowed start cannot carry a second command. The same set ShellToolCollection refuses under an allowlist.
    private static readonly char[] ChainingCharacters = [';', '&', '|', '`', '(', ')', '<', '>', '\n', '\r'];

    private AllowRule(string tool, string? argument, string? prefix, string? wholeText)
    {
        Tool = tool;
        Argument = argument;
        Prefix = prefix;
        WholeText = wholeText;
    }

    /// <summary>The tool's name, matched ignoring case.</summary>
    public string Tool { get; }

    /// <summary>The argument whose text decides; null when the rule covers every call of the tool.</summary>
    public string? Argument { get; }

    /// <summary>What the argument's text must start with, as whole words; null for another form of rule.</summary>
    public string? Prefix { get; }

    /// <summary>What the argument's text must be, exactly; null for another form of rule.</summary>
    public string? WholeText { get; }

    /// <summary>Covers every call of <paramref name="tool"/>.</summary>
    /// <exception cref="ArgumentException">The name is blank.</exception>
    public static AllowRule ForTool(string tool)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tool);
        return new AllowRule(tool.Trim(), null, null, null);
    }

    /// <summary>
    /// Covers the calls of <paramref name="tool"/> whose <paramref name="argument"/> starts with <paramref name="prefix"/> as whole
    /// words, ignoring case: <c>dotnet build</c> covers <c>dotnet build -c Release</c>, not <c>dotnet builder</c>.
    /// </summary>
    /// <remarks>
    /// It never covers text that holds <c>; &amp; | ` ( ) &lt; &gt;</c> or a line break, so an allowed start cannot carry a second
    /// command: <c>dotnet build; rm -rf .</c> is asked about. A prefix says what a command starts with, not what it does.
    /// </remarks>
    /// <exception cref="ArgumentException">A value is blank, or the prefix itself holds one of those characters; the message names the fix.</exception>
    public static AllowRule ForPrefix(string tool, string argument, string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tool);
        ArgumentException.ThrowIfNullOrWhiteSpace(argument);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        if (prefix.IndexOfAny(ChainingCharacters) >= 0)
        {
            throw new ArgumentException(
                $"The prefix '{prefix}' holds a character that chains or redirects commands, and a prefix rule never matches such text. " +
                $"Give the start of one command, or allow this exact text with {nameof(AllowRule)}.{nameof(ForWholeText)}.", nameof(prefix));
        }
        return new AllowRule(tool.Trim(), argument.Trim(), prefix.Trim(), null);
    }

    /// <summary>
    /// Covers the calls of <paramref name="tool"/> whose <paramref name="argument"/> is exactly <paramref name="text"/>, apart from
    /// white space around it. For one command you wrote yourself, such as a build-and-test line; it may chain commands.
    /// </summary>
    /// <exception cref="ArgumentException">A value is blank.</exception>
    public static AllowRule ForWholeText(string tool, string argument, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tool);
        ArgumentException.ThrowIfNullOrWhiteSpace(argument);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new AllowRule(tool.Trim(), argument.Trim(), null, text.Trim());
    }

    /// <summary>Whether the rule covers <paramref name="call"/>.</summary>
    public bool Matches(FunctionCallContent call)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (!string.Equals(call.Name, Tool, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (Argument is null)
        {
            return true;
        }
        if (TextOf(call, Argument)?.Trim() is not string text)
        {
            return false;
        }
        if (WholeText is not null)
        {
            return string.Equals(text, WholeText, StringComparison.Ordinal);
        }
        return text.IndexOfAny(ChainingCharacters) < 0
               && text.StartsWith(Prefix!, StringComparison.OrdinalIgnoreCase)
               && (text.Length == Prefix!.Length || char.IsWhiteSpace(text[Prefix.Length]));
    }

    /// <summary>The rule in a line a user can read, such as <c>RunShell starting with "dotnet build"</c>.</summary>
    public override string ToString() =>
        Prefix is not null ? $"{Tool} starting with \"{Prefix}\""
        : WholeText is not null ? $"{Tool} with exactly \"{WholeText}\""
        : $"every {Tool} call";

    /// <summary>
    /// The rule a session-wide answer to <paramref name="call"/> keeps when <paramref name="argument"/> is a command line: its first
    /// two words when the second is a subcommand (it does not start with <c>-</c>), else its first word. Null when the text is
    /// missing or chains commands, since no prefix rule would match it again.
    /// </summary>
    internal static AllowRule? ForCommandOf(FunctionCallContent call, string argument)
    {
        string? text = TextOf(call, argument);
        if (string.IsNullOrWhiteSpace(text) || text.IndexOfAny(ChainingCharacters) >= 0)
        {
            return null;
        }
        string[] words = text.Split((char[]?)null, 3, StringSplitOptions.RemoveEmptyEntries);
        string prefix = words.Length > 1 && !words[1].StartsWith('-') ? $"{words[0]} {words[1]}" : words[0];
        // A command typed with more than one space between its first words would not start with the prefix made here.
        return new AllowRule(call.Name, argument, prefix, null) is { } rule && rule.Matches(call) ? rule : null;
    }

    // The argument as text, as the model sent it: a string, or the JSON string the tool loop keeps.
    private static string? TextOf(FunctionCallContent call, string argument)
    {
        if (call.Arguments is null)
        {
            return null;
        }
        object? value = call.Arguments.FirstOrDefault(pair => string.Equals(pair.Key, argument, StringComparison.OrdinalIgnoreCase)).Value;
        return value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => null
        };
    }
}
