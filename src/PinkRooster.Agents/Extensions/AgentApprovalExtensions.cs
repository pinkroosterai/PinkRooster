using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents.Permissions;

// Declared in the package's root namespace by hand, like CreateAgent: a caller finds it with the root using alone.
namespace PinkRooster.Agents;

/// <summary>Running an agent whose tools need approval: the requests of a response, and the loop that answers them.</summary>
public static class AgentApprovalExtensions
{
    /// <summary>What a refused call tells the model when the callback gives no reason of its own.</summary>
    internal const string RefusedReason = "The user did not allow this.";

    /// <summary>The tool calls this response asks approval for, in order; empty when the run ended without asking.</summary>
    /// <exception cref="ArgumentNullException">The response is null.</exception>
    public static IReadOnlyList<ToolApprovalRequestContent> GetApprovalRequests(this AgentResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return [.. response.Messages.SelectMany(message => message.Contents).OfType<ToolApprovalRequestContent>()];
    }

    /// <summary>
    /// Runs the agent on <paramref name="message"/> and, each time the run ends with approval requests, asks <paramref name="approve"/>
    /// about every one and continues on the same session, until a run ends without asking. Returns that last run's response.
    /// </summary>
    /// <remarks>
    /// A refused call is not run and the model is told the user did not allow it (the overload whose callback returns an <see cref="ApprovalAnswer"/> can give another reason); a request for anything but a function call is refused
    /// without asking. Requests are asked one at a time, in order. Each continuation is a run of its own, so event handlers see a
    /// <c>RunCompleted</c> with <c>AwaitingApproval</c> before each question. MAF's <c>ToolApprovalAgent</c> ("don't ask again" rules)
    /// works below this: add it with the builder's <c>Use</c>, and only the requests it lets through reach the callback.
    /// </remarks>
    /// <param name="agent">The agent to run.</param>
    /// <param name="message">The user's message.</param>
    /// <param name="session">The session to run on; null starts a new one, which the continuations then share.</param>
    /// <param name="approve">Asked about each call that needs approval; true lets it run. A console's <c>ConfirmToolCallAsync</c> fits as it is.</param>
    /// <param name="options">Options for every run of the loop.</param>
    /// <param name="cancellationToken">Cancels the runs and the questions.</param>
    /// <exception cref="ArgumentNullException">The agent or the callback is null.</exception>
    /// <exception cref="ArgumentException">The message is blank.</exception>
    public static Task<AgentResponse> RunWithApprovalsAsync(
        this AIAgent agent,
        string message,
        AgentSession? session,
        Func<FunctionCallContent, CancellationToken, Task<bool>> approve,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return RunWithApprovalsAsync(agent, [new ChatMessage(ChatRole.User, message)], session, approve, options, cancellationToken);
    }

    /// <summary>As <see cref="RunWithApprovalsAsync(AIAgent, string, AgentSession?, Func{FunctionCallContent, CancellationToken, Task{bool}}, AgentRunOptions?, CancellationToken)"/>, for messages.</summary>
    /// <param name="agent">The agent to run.</param>
    /// <param name="messages">The messages of the first run.</param>
    /// <param name="session">The session to run on; null starts a new one, which the continuations then share.</param>
    /// <param name="approve">Asked about each call that needs approval; true lets it run.</param>
    /// <param name="options">Options for every run of the loop.</param>
    /// <param name="cancellationToken">Cancels the runs and the questions.</param>
    /// <exception cref="ArgumentNullException">The agent, the messages or the callback is null.</exception>
    public static Task<AgentResponse> RunWithApprovalsAsync(
        this AIAgent agent,
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        Func<FunctionCallContent, CancellationToken, Task<bool>> approve,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(approve);
        return RunWithApprovalsAsync(agent, messages, session, YesOrNo(approve), options, cancellationToken);
    }

    /// <summary>
    /// As <see cref="RunWithApprovalsAsync(AIAgent, string, AgentSession?, Func{FunctionCallContent, CancellationToken, Task{bool}}, AgentRunOptions?, CancellationToken)"/>,
    /// for a callback that can say why it refuses, such as a <see cref="PermissionPolicy"/>'s <see cref="PermissionPolicy.AnswerAsync"/>:
    /// the model reads the reason as the tool's answer.
    /// </summary>
    /// <param name="agent">The agent to run.</param>
    /// <param name="message">The user's message.</param>
    /// <param name="session">The session to run on; null starts a new one, which the continuations then share.</param>
    /// <param name="answer">Asked about each call that needs approval.</param>
    /// <param name="options">Options for every run of the loop.</param>
    /// <param name="cancellationToken">Cancels the runs and the questions.</param>
    /// <exception cref="ArgumentNullException">The agent or the callback is null.</exception>
    /// <exception cref="ArgumentException">The message is blank.</exception>
    public static Task<AgentResponse> RunWithApprovalsAsync(
        this AIAgent agent,
        string message,
        AgentSession? session,
        Func<FunctionCallContent, CancellationToken, Task<ApprovalAnswer>> answer,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return RunWithApprovalsAsync(agent, [new ChatMessage(ChatRole.User, message)], session, answer, options, cancellationToken);
    }

    /// <summary>As <see cref="RunWithApprovalsAsync(AIAgent, string, AgentSession?, Func{FunctionCallContent, CancellationToken, Task{ApprovalAnswer}}, AgentRunOptions?, CancellationToken)"/>, for messages.</summary>
    /// <param name="agent">The agent to run.</param>
    /// <param name="messages">The messages of the first run.</param>
    /// <param name="session">The session to run on; null starts a new one, which the continuations then share.</param>
    /// <param name="answer">Asked about each call that needs approval.</param>
    /// <param name="options">Options for every run of the loop.</param>
    /// <param name="cancellationToken">Cancels the runs and the questions.</param>
    /// <exception cref="ArgumentNullException">The agent, the messages or the callback is null.</exception>
    public static Task<AgentResponse> RunWithApprovalsAsync(
        this AIAgent agent,
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        Func<FunctionCallContent, CancellationToken, Task<ApprovalAnswer>> answer,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(answer);
        return RunWithApprovalsAsync(agent, messages, session, (request, token) => AnswerAsync(request, answer, token), options, cancellationToken);
    }

    /// <summary>
    /// Streams the agent's run on <paramref name="message"/> and, each time a run ends with approval requests, asks <paramref name="approve"/>
    /// about every one and streams the continuation on the same session, until a run ends without asking.
    /// </summary>
    /// <remarks>
    /// The updates of every run are yielded as they arrive, the approval requests among them. The rules for a refused call and for
    /// what is asked are those of <see cref="RunWithApprovalsAsync(AIAgent, string, AgentSession?, Func{FunctionCallContent, CancellationToken, Task{bool}}, AgentRunOptions?, CancellationToken)"/>.
    /// </remarks>
    /// <param name="agent">The agent to run.</param>
    /// <param name="message">The user's message.</param>
    /// <param name="session">The session to run on; null starts a new one, which the continuations then share.</param>
    /// <param name="approve">Asked about each call that needs approval; true lets it run.</param>
    /// <param name="options">Options for every run of the loop.</param>
    /// <param name="cancellationToken">Cancels the runs and the questions.</param>
    /// <exception cref="ArgumentNullException">The agent or the callback is null.</exception>
    /// <exception cref="ArgumentException">The message is blank.</exception>
    public static IAsyncEnumerable<AgentResponseUpdate> RunStreamingWithApprovalsAsync(
        this AIAgent agent,
        string message,
        AgentSession? session,
        Func<FunctionCallContent, CancellationToken, Task<bool>> approve,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(approve);
        return StreamAsync(agent, [new ChatMessage(ChatRole.User, message)], session, YesOrNo(approve), options, cancellationToken);
    }

    /// <summary>
    /// As <see cref="RunStreamingWithApprovalsAsync(AIAgent, string, AgentSession?, Func{FunctionCallContent, CancellationToken, Task{bool}}, AgentRunOptions?, CancellationToken)"/>,
    /// for a callback that can say why it refuses, such as a <see cref="PermissionPolicy"/>'s <see cref="PermissionPolicy.AnswerAsync"/>.
    /// </summary>
    /// <param name="agent">The agent to run.</param>
    /// <param name="message">The user's message.</param>
    /// <param name="session">The session to run on; null starts a new one, which the continuations then share.</param>
    /// <param name="answer">Asked about each call that needs approval.</param>
    /// <param name="options">Options for every run of the loop.</param>
    /// <param name="cancellationToken">Cancels the runs and the questions.</param>
    /// <exception cref="ArgumentNullException">The agent or the callback is null.</exception>
    /// <exception cref="ArgumentException">The message is blank.</exception>
    public static IAsyncEnumerable<AgentResponseUpdate> RunStreamingWithApprovalsAsync(
        this AIAgent agent,
        string message,
        AgentSession? session,
        Func<FunctionCallContent, CancellationToken, Task<ApprovalAnswer>> answer,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(answer);
        return StreamAsync(agent, [new ChatMessage(ChatRole.User, message)], session, (request, token) => AnswerAsync(request, answer, token), options, cancellationToken);
    }

    /// <summary>The loop with the answering left to the caller, for one that has more to say than yes or no, such as the sub-agent collection.</summary>
    internal static async Task<AgentResponse> RunWithApprovalsAsync(
        AIAgent agent,
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        Func<ToolApprovalRequestContent, CancellationToken, Task<AIContent>> answer,
        AgentRunOptions? options,
        CancellationToken cancellationToken)
    {
        // The answers go back on the session that asked, so a run without one gets one here.
        session ??= await agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        AgentResponse response = await agent.RunAsync(messages, session, options, cancellationToken).ConfigureAwait(false);
        while (response.GetApprovalRequests() is { Count: > 0 } requests)
        {
            ChatMessage answers = await AnswerAllAsync(requests, answer, cancellationToken).ConfigureAwait(false);
            response = await agent.RunAsync(answers, session, options, cancellationToken).ConfigureAwait(false);
        }
        return response;
    }

    private static async IAsyncEnumerable<AgentResponseUpdate> StreamAsync(
        AIAgent agent,
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        Func<ToolApprovalRequestContent, CancellationToken, Task<AIContent>> answer,
        AgentRunOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        session ??= await agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        IEnumerable<ChatMessage> input = messages;
        while (true)
        {
            List<ToolApprovalRequestContent> requests = [];
            await foreach (AgentResponseUpdate update in agent.RunStreamingAsync(input, session, options, cancellationToken).ConfigureAwait(false))
            {
                requests.AddRange(update.Contents.OfType<ToolApprovalRequestContent>());
                yield return update;
            }
            if (requests.Count == 0)
            {
                yield break;
            }
            input = [await AnswerAllAsync(requests, answer, cancellationToken).ConfigureAwait(false)];
        }
    }

    private static async Task<ChatMessage> AnswerAllAsync(
        IReadOnlyList<ToolApprovalRequestContent> requests,
        Func<ToolApprovalRequestContent, CancellationToken, Task<AIContent>> answer,
        CancellationToken cancellationToken)
    {
        List<AIContent> answers = [];
        foreach (ToolApprovalRequestContent request in requests)
        {
            answers.Add(await answer(request, cancellationToken).ConfigureAwait(false));
        }
        return new ChatMessage(ChatRole.User, answers);
    }

    /// <summary>The content that answers <paramref name="request"/>; a request for anything but a function call is refused without asking.</summary>
    internal static async Task<AIContent> AnswerAsync(
        ToolApprovalRequestContent request,
        Func<FunctionCallContent, CancellationToken, Task<ApprovalAnswer>> answer,
        CancellationToken cancellationToken)
    {
        ApprovalAnswer given = request.ToolCall is FunctionCallContent call ? await answer(call, cancellationToken).ConfigureAwait(false) : default;
        return request.CreateResponse(given.Approved, given.Approved ? null : string.IsNullOrWhiteSpace(given.Reason) ? RefusedReason : given.Reason);
    }

    /// <summary>A yes-or-no callback as one that answers a request; a no carries no reason of its own.</summary>
    internal static Func<ToolApprovalRequestContent, CancellationToken, Task<AIContent>> YesOrNo(Func<FunctionCallContent, CancellationToken, Task<bool>> approve) =>
        (request, token) => AnswerAsync(request, async (call, cancellation) => new ApprovalAnswer(await approve(call, cancellation).ConfigureAwait(false)), token);
}
