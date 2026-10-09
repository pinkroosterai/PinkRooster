using PinkRooster.Agents.Eventing;
using PinkRooster.Agents.Permissions;
using PinkRooster.SpectreConsole;
using PinkRooster.ToolCollections.BuiltIn.AskUserQuestion;

namespace PinkRooster.Samples.Tests.CodingAgent;

/// <summary>
/// An <see cref="IAgentConsole"/> that records what the showcase wrote and answers from queues: typed lines, approval answers
/// (allow when the queue is empty) and question answers (the first option when the queue is empty).
/// </summary>
internal sealed class RecordingAgentConsole : IAgentConsole
{
    private readonly object gate = new();
    private readonly System.Text.StringBuilder output = new();
    private readonly List<AgentEvent> events = [];

    public bool IsInteractive { get; init; } = true;

    public Queue<string> Inputs { get; } = [];

    public Queue<ApprovalChoice> Approvals { get; } = [];

    public Queue<string> Picks { get; } = [];

    public List<ApprovalQuestion> ApprovalsAsked { get; } = [];

    public List<UserQuestion> QuestionsAsked { get; } = [];

    public IReadOnlyList<AgentEvent> Events
    {
        get
        {
            lock (gate)
            {
                return [.. events];
            }
        }
    }

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
            events.Add(item);
        }
    }

    public Task<ApprovalChoice> ConfirmToolCallAsync(ApprovalQuestion question, CancellationToken cancellationToken)
    {
        ApprovalsAsked.Add(question);
        return Task.FromResult(Approvals.TryDequeue(out ApprovalChoice choice) ? choice : ApprovalChoice.Allow);
    }

    public Task<IReadOnlyList<UserQuestionAnswer>> AskAsync(IReadOnlyList<UserQuestion> questions, CancellationToken cancellationToken)
    {
        QuestionsAsked.AddRange(questions);
        return Task.FromResult<IReadOnlyList<UserQuestionAnswer>>(
            [.. questions.Select(question => new UserQuestionAnswer([Picks.TryDequeue(out string? pick) ? pick : question.Options[0].Label]))]);
    }

    public Task<string?> ReadLineAsync(string prompt, CancellationToken cancellationToken) =>
        Task.FromResult(cancellationToken.IsCancellationRequested ? null : Inputs.TryDequeue(out string? input) ? input : null);
}
