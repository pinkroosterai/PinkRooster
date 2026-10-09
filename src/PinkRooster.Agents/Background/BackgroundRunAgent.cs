using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace PinkRooster.Agents.Background;

/// <summary>
/// Gives an agent its inbox, makes background tasks belong to the run, and keeps the run going while it has any: MAF's
/// <see cref="LoopAgent"/> around the builder's agent, with <see cref="BackgroundWake"/> deciding after each turn whether to wait,
/// call the model again or end. When the run ends, whatever is still running is stopped and waited for. A run that ends with an
/// approval request is paused instead, and the run that carries the answers takes its tasks over.
/// </summary>
/// <remarks>
/// A run without a session gets one, because the tasks are kept by session. <c>RunAsync</c> returns the last turn's reply; a streamed
/// run yields every turn.
/// </remarks>
internal sealed class BackgroundRunAgent(AIAgent innerAgent, BackgroundOptions options, ILoggerFactory? loggerFactory)
    : DelegatingAIAgent(new LoopAgent(innerAgent, new BackgroundWake(), LoopOptions(), loggerFactory))
{
    private readonly ILogger? logger = loggerFactory?.CreateLogger<BackgroundRunAgent>();
    // Per agent, so two agents that share a session each keep their own tasks.
    private readonly ConditionalWeakTable<AgentSession, BackgroundTaskStore> stores = [];

    /// <summary>The store this agent has for <paramref name="session"/>, or null when it never ran on it.</summary>
    public BackgroundTaskStore? Find(AgentSession session) => stores.TryGetValue(session, out BackgroundTaskStore? store) ? store : null;

    protected override async Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? runOptions = null, CancellationToken cancellationToken = default)
    {
        ChatMessage[] input = [.. messages];
        session ??= await CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        BackgroundTaskStore store = await OpenAsync(input, session).ConfigureAwait(false);

        bool awaitsApproval = false;
        try
        {
            BackgroundTaskStore.Current = store;
            AgentResponse response = await base.RunCoreAsync(input, session, runOptions, cancellationToken).ConfigureAwait(false);
            awaitsApproval = response.Messages.SelectMany(message => message.Contents).OfType<ToolApprovalRequestContent>().Any();
            return response;
        }
        finally
        {
            await CloseAsync(store, awaitsApproval).ConfigureAwait(false);
        }
    }

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? runOptions = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ChatMessage[] input = [.. messages];
        session ??= await CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        BackgroundTaskStore store = await OpenAsync(input, session).ConfigureAwait(false);

        bool awaitsApproval = false;
        try
        {
            BackgroundTaskStore.Current = store;
            await foreach (AgentResponseUpdate update in base.RunCoreStreamingAsync(input, session, runOptions, cancellationToken).ConfigureAwait(false))
            {
                awaitsApproval |= update.Contents.OfType<ToolApprovalRequestContent>().Any();
                yield return update;
                // The iterator resumes on the consumer's async flow, which does not hold the store.
                BackgroundTaskStore.Current = store;
            }
        }
        finally
        {
            await CloseAsync(store, awaitsApproval).ConfigureAwait(false);
        }
    }

    private async Task<BackgroundTaskStore> OpenAsync(ChatMessage[] input, AgentSession session)
    {
        MessageInjectingChatClient inbox = GetService<MessageInjectingChatClient>()
            ?? throw new InvalidOperationException(
                $"The agent has an inbox but no {nameof(MessageInjectingChatClient)} in its client pipeline. " +
                $"Leave {nameof(ChatClientAgentOptions.EnableMessageInjection)} on in {nameof(AgentBuilder.ConfigureAgentOptions)}.");
        // A foreground tool that throws shows the model its message only when the tool loop is told to; a background one follows it.
        bool showErrorDetails = GetService<FunctionInvokingChatClient>()?.IncludeDetailedErrors ?? false;
        BackgroundTaskStore store = stores.GetValue(session, _ => new BackgroundTaskStore());
        await store.BeginRunAsync(options, AnswersApprovals(input), showErrorDetails, message => inbox.EnqueueMessagesAsync(session, [message]), logger).ConfigureAwait(false);
        return store;
    }

    private static Task CloseAsync(BackgroundTaskStore store, bool awaitsApproval)
    {
        if (awaitsApproval)
        {
            store.Pause();
            return Task.CompletedTask;
        }
        return store.EndRunAsync();
    }

    // The step loop's rule for input that continues a paused run: nothing but approval answers.
    private static bool AnswersApprovals(ChatMessage[] input) =>
        input.Length > 0 && input.All(message => message.Contents.Count > 0 && message.Contents.All(content => content is ToolApprovalResponseContent));

    private static LoopAgentOptions LoopOptions() => new()
    {
        // BackgroundWake counts the wakes itself, because a call made for a host's message is not one.
        MaxIterations = int.MaxValue,
        NonStreamingReturnsLastResponseOnly = true,
        FreshContextPerIteration = false,
        ExcludeOnBehalfOfMessages = true
    };
}
