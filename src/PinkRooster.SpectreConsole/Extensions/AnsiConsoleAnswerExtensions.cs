using NTokenizers.Extensions.Spectre.Console;
using Spectre.Console;

namespace PinkRooster.SpectreConsole;

/// <summary>Draws an agent's answer on a Spectre.Console terminal.</summary>
public static class AnsiConsoleAnswerExtensions
{
    /// <summary>Writes an answer a model wrote in markdown: headings, emphasis, lists, tables and code blocks, drawn when the terminal can render them.</summary>
    /// <remarks>
    /// The markdown renderer redraws tables and code blocks in place with cursor moves, so it runs only on a terminal with ANSI that is
    /// interactive; on any other the answer is written as plain text, so a redirected or piped run still gets it whole. The answer is
    /// drawn whole, not as it streams; <see cref="AgentConsole"/> draws an answer while it streams. The renderer (NTokenizers 2.4.0) does its work on a thread-pool thread, so a failure inside it, such as
    /// a console that cannot be written to, ends the process and cannot be caught here; malformed markdown from a model draws without an error.
    /// </remarks>
    /// <param name="console">The console to write to, such as <c>AnsiConsole.Console</c>.</param>
    /// <param name="answer">The answer, such as <c>response.Text</c>. Null, empty or blank writes nothing.</param>
    /// <example>
    /// <code>
    /// AgentResponse response = await agent.RunAsync(question);
    /// AnsiConsole.Console.WriteAnswer(response.Text);
    /// </code>
    /// </example>
    public static void WriteAnswer(this IAnsiConsole console, string? answer)
    {
        ArgumentNullException.ThrowIfNull(console);
        if (string.IsNullOrWhiteSpace(answer))
        {
            return;
        }

        string text = answer.TrimEnd();
        if (!console.CanRenderMarkdown())
        {
            console.WriteLine(text);
            return;
        }

        console.WriteMarkdown(AnswerMarkdown.Prepare(text, console.Profile.Width));
        console.WriteLine();
    }
}
