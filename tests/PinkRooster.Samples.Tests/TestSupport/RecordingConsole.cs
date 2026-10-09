using Microsoft.Extensions.AI;
using PinkRooster.Agents.Eventing;
using PinkRooster.Agents.Permissions;
using PinkRooster.Samples.Terminal;
using PinkRooster.ToolCollections.BuiltIn.AskUserQuestion;

namespace PinkRooster.Samples.Tests.TestSupport;

/// <summary>An <see cref="ISampleConsole"/> that records what a sample wrote and answers approvals and questions from queues (default: approve, first option).</summary>
internal sealed class RecordingConsole : ISampleConsole
{
    private readonly object gate = new();
    private readonly System.Text.StringBuilder output = new();

    public bool IsInteractive { get; init; } = true;

    public Queue<bool> Approvals { get; } = [];

    public Queue<string> Inputs { get; } = [];

    public List<string> ApprovalsAsked { get; } = [];

    public List<AgentEvent> Events { get; } = [];

    public List<Exception> Errors { get; } = [];

    /// <summary>Everything written with <see cref="WriteLine"/> and <see cref="Write"/>, line breaks as <c>\n</c>.</summary>
    public string Output
    {
        get
        {
            lock (gate)
            {
                return output.ToString();
            }
        }
    }

    public void WriteLine(string text = "")
    {
        lock (gate)
        {
            output.Append(text).Append('\n');
        }
    }

    public void WriteAnswer(string text) => WriteLine(text);

    public void Write(string text)
    {
        lock (gate)
        {
            output.Append(text);
        }
    }

    public void WriteEvent(AgentEvent item)
    {
        lock (gate)
        {
            Events.Add(item);
        }
    }

    public Task<ApprovalChoice> ConfirmToolCallAsync(ApprovalQuestion question, CancellationToken cancellationToken)
    {
        ApprovalsAsked.Add(question.Call.Name);
        return Task.FromResult(Approvals.Count == 0 || Approvals.Dequeue() ? ApprovalChoice.Allow : ApprovalChoice.Skip);
    }

    public Task<IReadOnlyList<UserQuestionAnswer>> AskAsync(IReadOnlyList<UserQuestion> questions, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<UserQuestionAnswer>>([.. questions.Select(question => new UserQuestionAnswer([question.Options[0].Label]))]);

    public Task<string?> ReadLineAsync(string prompt, CancellationToken cancellationToken) =>
        Task.FromResult(Inputs.TryDequeue(out string? input) ? input : null);

    public void WriteError(Exception error) => Errors.Add(error);
}
