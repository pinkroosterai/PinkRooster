using Microsoft.Extensions.AI;
using PinkRooster.Agents.Eventing;
using PinkRooster.Agents.Permissions;
using PinkRooster.ToolCollections.BuiltIn.AskUserQuestion;

namespace PinkRooster.SpectreConsole;

/// <summary>What an app needs from a screen to show an agent's run and ask the user things, so code written against it runs on a terminal and in a test.</summary>
public interface IAgentConsole
{
    /// <summary>False when nobody can answer an approval or a question: input is redirected or the terminal cannot prompt.</summary>
    bool IsInteractive { get; }

    /// <summary>Writes a line of text.</summary>
    void WriteLine(string text = "");

    /// <summary>Writes an answer the model wrote in markdown, formatted: headings, emphasis, lists, tables and code blocks.</summary>
    void WriteAnswer(string text);

    /// <summary>Writes a piece of text without ending the line, for a streamed answer.</summary>
    void Write(string text);

    /// <summary>Shows one agent event: reasoning, answer text, a tool call or its result. Called from the agent's threads; subscribe with <c>agent.OnEvent(console.WriteEvent)</c>.</summary>
    void WriteEvent(AgentEvent item);

    /// <summary>
    /// Asks whether a tool call may run: allow it, skip it or, when the question offers a <see cref="ApprovalQuestion.SessionRule"/>, allow
    /// what that rule covers for the rest of the session. Returns <see cref="ApprovalChoice.Skip"/> when nobody can answer.
    /// </summary>
    /// <remarks>
    /// It fits a <see cref="PermissionPolicy"/> as the function that asks. For a plain yes or no, the extension
    /// <c>ConfirmToolCallAsync(call, cancellationToken)</c> asks without the third answer.
    /// </remarks>
    Task<ApprovalChoice> ConfirmToolCallAsync(ApprovalQuestion question, CancellationToken cancellationToken);

    /// <summary>Shows the questions and returns one answer per question, in order.</summary>
    /// <exception cref="InvalidOperationException">The console is not interactive, so nobody can answer.</exception>
    Task<IReadOnlyList<UserQuestionAnswer>> AskAsync(IReadOnlyList<UserQuestion> questions, CancellationToken cancellationToken);

    /// <summary>Reads a line of user input with a prompt, or returns null when input is ended or not interactive.</summary>
    Task<string?> ReadLineAsync(string prompt, CancellationToken cancellationToken);
}
