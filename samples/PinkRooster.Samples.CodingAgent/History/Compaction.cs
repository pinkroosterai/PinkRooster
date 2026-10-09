#pragma warning disable MAAI001 // MAF's compaction strategies are experimental in 1.24.0.
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;

namespace PinkRooster.Samples.CodingAgent.History;

/// <summary>
/// Shortens what is sent to the model on a long conversation, in two stages: older tool results are collapsed first, and a summary
/// replaces the oldest part after. The saved session is never shortened: this works on a request on its way to the model.
/// </summary>
/// <remarks>
/// <para>
/// The stages are MAF's <see cref="ToolResultCompactionStrategy"/> and <see cref="SummarizationCompactionStrategy"/>, run with
/// <see cref="CompactionProvider.CompactAsync"/>. MAF's provider itself cannot be used here: it steps aside for an agent whose
/// history is saved after every model call, which an agent with an inbox is.
/// </para>
/// <para>
/// What a compaction produced is kept per session, in memory, and put in place again at the next request. Without that every
/// model call past the trigger would pay for a new summary, because each request starts from the whole saved history.
/// </para>
/// </remarks>
public sealed class Compaction
{
    // The newest exchanges stay as they are, whatever the stage.
    private const int KeptToolGroups = 2;
    private const int KeptGroups = 4;

    private readonly ConditionalWeakTable<AgentSession, Kept> kept = [];
    private readonly Action<string> warn;
    private readonly SummaryClient summarizer;
    private readonly CompactionStrategy? automatic;
    private readonly CompactionStrategy forced;

    /// <param name="summarizer">The model that writes the summary.</param>
    /// <param name="contextSize">The model's context window in tokens; null when it is not known, and then nothing is compacted unless it is asked for.</param>
    /// <param name="startAt">The share of the context window at which compaction starts, such as 0.75.</param>
    /// <param name="warn">Told when a summary could not be made and a request went out whole.</param>
    public Compaction(IChatClient summarizer, int? contextSize, double startAt, Action<string> warn)
    {
        this.warn = warn;
        this.summarizer = new SummaryClient(summarizer);
        if (contextSize is int size)
        {
            TriggerTokens = (int)(size * startAt);
            CompactionTrigger past = CompactionTriggers.TokensExceed(TriggerTokens.Value);
            automatic = new PipelineCompactionStrategy([
                new ToolResultCompactionStrategy(past, KeptToolGroups),
                // Down to half the trigger, so the next few exchanges fit before a summary is needed again.
                new SummarizationCompactionStrategy(this.summarizer, past, KeptGroups, target: CompactionTriggers.TokensBelow(TriggerTokens.Value / 2))]);
        }
        forced = new PipelineCompactionStrategy([
            new ToolResultCompactionStrategy(CompactionTriggers.Always, KeptToolGroups),
            new SummarizationCompactionStrategy(this.summarizer, CompactionTriggers.Always, KeptGroups)]);
    }

    /// <summary>The estimated size of a request, in tokens, past which it is compacted; null when the model's context size is not known.</summary>
    public int? TriggerTokens { get; }

    /// <summary>Makes the next request of <paramref name="session"/> compact whatever its size: the <c>/compact</c> command.</summary>
    public void CompactNext(AgentSession session) => kept.GetOrCreateValue(session).Forced = true;

    /// <summary>The request to send for <paramref name="messages"/>: with what an earlier compaction kept put in place, and compacted again when it is past the trigger or was asked for.</summary>
    public async Task<IEnumerable<ChatMessage>> ShortenAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken)
    {
        if (AIAgent.CurrentRunContext?.Session is not AgentSession session)
        {
            return messages;
        }

        Kept state = kept.GetOrCreateValue(session);
        List<ChatMessage> request = [.. messages];
        if (state.Covered > request.Count)
        {
            // A history shorter than what was kept is not the conversation it was kept for.
            state.Covered = 0;
            state.Replacement = [];
        }
        // The history only grows at its end, so the kept text stands for its first messages, however many came after.
        request = [.. state.Replacement, .. request.Skip(state.Covered)];

        CompactionStrategy? strategy = state.Forced ? forced : automatic;
        if (strategy is null)
        {
            return request;
        }

        List<ChatMessage> compacted;
        summarizer.Failure = null;
        try
        {
            compacted = [.. await CompactionProvider.CompactAsync(strategy, request, logger: null, cancellationToken).ConfigureAwait(false)];
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            summarizer.Failure ??= error;
            compacted = request;
        }
        state.Forced = false;
        if (summarizer.Failure is Exception failure)
        {
            warn($"The conversation could not be summarised ({failure.Message}), so this request goes out whole.");
            return request;
        }

        // What the strategies left alone is the same messages at the end; everything before it is what they made.
        int untouched = 0;
        while (untouched < compacted.Count && untouched < request.Count && ReferenceEquals(compacted[^(untouched + 1)], request[^(untouched + 1)]))
        {
            untouched++;
        }
        int replaced = request.Count - untouched;
        bool changed = untouched < request.Count || compacted.Count != request.Count;
        if (changed && replaced >= state.Replacement.Count)
        {
            state.Covered += replaced - state.Replacement.Count;
            state.Replacement = compacted[..(compacted.Count - untouched)];
        }
        return compacted;
    }

    // What one session's compaction stands for: the first Covered messages of its history, as Replacement.
    private sealed class Kept
    {
        public int Covered { get; set; }

        public List<ChatMessage> Replacement { get; set; } = [];

        public bool Forced { get; set; }
    }

    // The summarizer, watched: a strategy may go on without a summary when the call for it fails, and the user is told either way.
    private sealed class SummaryClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
    {
        public Exception? Failure { get; set; }

        public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            try
            {
                return await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                Failure = error;
                throw;
            }
        }
    }
}
