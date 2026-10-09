using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;

namespace PinkRooster.Agents.Eventing;

/// <summary>
/// Publishes what each model call returns — reasoning and answer text as deltas and completed blocks, and the tool calls the model
/// asks for — while a <see cref="RunScope"/> is open.
/// </summary>
/// <remarks>
/// It sits below the tool loop, so every model call of a run is reported as it happens. A non-streaming call publishes one delta per
/// block, then the block, so a subscriber written against deltas works for both kinds of call.
/// </remarks>
internal sealed class EventingChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        RunScope? scope = RunScope.Current;
        if (scope is null)
        {
            return await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        }

        long started = Stopwatch.GetTimestamp();
        ChatResponse response;
        try
        {
            scope.Publish(() => new ModelCallStarted());
            response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            scope.Publish(() => new ModelCallCompleted(null, null, null, Stopwatch.GetElapsedTime(started), error));
            throw;
        }

        // Outside the try: a handler that throws on a terminal event must not be told the call failed.
        Blocks blocks = new(scope);
        foreach (AIContent content in response.Messages.Where(message => message.Role == ChatRole.Assistant).SelectMany(message => message.Contents))
        {
            blocks.Add(content);
        }
        blocks.Close();
        UsageDetails? responseUsage = RunScope.Snapshot(response.Usage);
        scope.AddUsage(responseUsage);
        scope.Publish(() => new ModelCallCompleted(response.ModelId, response.FinishReason, responseUsage, Stopwatch.GetElapsedTime(started), null));
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Read once: after a yield the iterator resumes on its consumer's flow.
        RunScope? scope = RunScope.Current;
        if (scope is null)
        {
            await foreach (ChatResponseUpdate update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
            {
                yield return update;
            }
            yield break;
        }

        long started = Stopwatch.GetTimestamp();
        Blocks blocks = new(scope);
        UsageDetails? usage = null;
        string? modelId = null;
        ChatFinishReason? finishReason = null;
        // Set before the call's ModelCallCompleted is published, so the finally below publishes one only for a stream left unfinished.
        bool completed = false;
        try
        {
            IAsyncEnumerator<ChatResponseUpdate> updates;
            try
            {
                scope.Publish(() => new ModelCallStarted());
                updates = base.GetStreamingResponseAsync(messages, options, cancellationToken).GetAsyncEnumerator(cancellationToken);
            }
            catch (Exception error)
            {
                completed = true;
                scope.Publish(() => new ModelCallCompleted(null, null, usage, Stopwatch.GetElapsedTime(started), error));
                throw;
            }

            await using (updates.ConfigureAwait(false))
            {
                while (true)
                {
                    ChatResponseUpdate update;
                    try
                    {
                        if (!await updates.MoveNextAsync().ConfigureAwait(false))
                        {
                            break;
                        }
                        update = updates.Current;
                        modelId = update.ModelId ?? modelId;
                        finishReason = update.FinishReason ?? finishReason;
                        foreach (AIContent content in update.Contents)
                        {
                            if (content is UsageContent used)
                            {
                                (usage ??= new()).Add(used.Details);
                            }
                            blocks.Add(content);
                        }
                    }
                    catch (Exception error)
                    {
                        // A failed call completes no open block.
                        completed = true;
                        scope.Publish(() => new ModelCallCompleted(modelId, finishReason, usage, Stopwatch.GetElapsedTime(started), error));
                        scope.AddUsage(usage);
                        throw;
                    }
                    yield return update;
                }
            }

            blocks.Close();
            completed = true;
            scope.AddUsage(usage);
            scope.Publish(() => new ModelCallCompleted(modelId, finishReason, usage, Stopwatch.GetElapsedTime(started), null));
        }
        finally
        {
            // The consumer stopped reading: the call ends with the usage that arrived, and no open block is completed.
            if (!completed)
            {
                completed = true;
                scope.AddUsage(usage);
                scope.Publish(() => new ModelCallCompleted(modelId, finishReason, usage, Stopwatch.GetElapsedTime(started), null));
            }
        }
    }

    /// <summary>Groups reasoning and answer text into blocks: a block ends where the other kind, a tool call or the model call begins or ends.</summary>
    private sealed class Blocks(RunScope scope)
    {
        private readonly StringBuilder text = new();
        private bool reasoning;

        public void Add(AIContent content)
        {
            switch (content)
            {
                case TextReasoningContent thought when !string.IsNullOrEmpty(thought.Text):
                    Continue(isReasoning: true);
                    text.Append(thought.Text);
                    scope.Publish(() => new ReasoningDelta(thought.Text));
                    break;
                case TextContent answer when !string.IsNullOrEmpty(answer.Text):
                    Continue(isReasoning: false);
                    text.Append(answer.Text);
                    scope.Publish(() => new AssistantTextDelta(answer.Text));
                    break;
                case FunctionCallContent call when !call.InformationalOnly:
                    Close();
                    scope.Publish(() => new ToolCallRequested(call.CallId, call.Name, Arguments.Of(call.Arguments)));
                    break;
            }
        }

        public void Close()
        {
            if (text.Length == 0)
            {
                return;
            }
            string block = text.ToString();
            text.Clear();
            scope.Publish(reasoning
                ? () => new ReasoningCompleted(block)
                : () => new AssistantTextCompleted(block));
        }

        private void Continue(bool isReasoning)
        {
            if (reasoning != isReasoning)
            {
                Close();
            }
            reasoning = isReasoning;
        }
    }
}
