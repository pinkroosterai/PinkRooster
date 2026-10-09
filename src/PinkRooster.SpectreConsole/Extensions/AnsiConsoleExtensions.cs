using Spectre.Console;

namespace PinkRooster.SpectreConsole;

/// <summary>Checks and prompts on a Spectre.Console terminal that do not need an <see cref="AgentConsole"/>.</summary>
public static class AnsiConsoleExtensions
{
    /// <summary>True when the terminal can draw markdown: it supports ANSI and is interactive.</summary>
    /// <remarks>The markdown renderer redraws tables and code blocks in place with cursor moves; without them every redraw would be printed.</remarks>
    /// <param name="console">The console to check, such as <c>AnsiConsole.Console</c>.</param>
    public static bool CanRenderMarkdown(this IAnsiConsole console)
    {
        ArgumentNullException.ThrowIfNull(console);
        return console.Profile.Capabilities.Ansi && console.Profile.Capabilities.Interactive;
    }

    /// <summary>Reads one line after a prompt, and returns null instead of throwing when nobody can answer.</summary>
    /// <param name="console">The console to prompt on.</param>
    /// <param name="prompt">The prompt text; it is written as plain text, so brackets in it are safe.</param>
    /// <param name="cancellationToken">Cancelling returns null.</param>
    /// <param name="style">A Spectre.Console style for the prompt text, such as <c>#ff5faf</c> or <c>bold red</c>; null leaves it unstyled.</param>
    /// <returns>The line, empty if the user pressed Enter at once; null when the terminal is not interactive, the call was cancelled or input has ended.</returns>
    public static async Task<string?> AskOrNullAsync(this IAnsiConsole console, string prompt, CancellationToken cancellationToken = default, string? style = null)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(prompt);
        if (!console.Profile.Capabilities.Interactive)
        {
            return null;
        }

        string escaped = Markup.Escape(prompt);
        try
        {
            return await console.PromptAsync(
                new TextPrompt<string>(style is null ? escaped : $"[{style}]{escaped}[/]").AllowEmpty(),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
