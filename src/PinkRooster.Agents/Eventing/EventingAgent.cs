using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace PinkRooster.Agents.Eventing;

/// <summary>
/// Opens a <see cref="RunScope"/> for each run and publishes what only the run level sees: its start and end, approval requests
/// in its output, and refusals in its input.
/// </summary>
/// <remarks>Approval requests are made by MAF's tool loop above the model client, so no client below the loop sees them.</remarks>
internal sealed class EventingAgent(AIAgent innerAgent, EventPublisher publisher) : DelegatingAIAgent(innerAgent)
{

    protected override async Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        ChatMessage[] input = [.. messages];
        RunScope scope = Start(input);
        AgentResponse response;
        bool awaitingApproval = false;
        try
        {
            response = await base.RunCoreAsync(input, session, options, cancellationToken).ConfigureAwait(false);
            foreach (ToolApprovalRequestContent request in response.Messages.SelectMany(message => message.Contents).OfType<ToolApprovalRequestContent>())
            {
                PublishApprovalRequest(scope, request);
                awaitingApproval = true;
            }
        }
        catch (Exception error)
        {
            PublishFailure(scope, error, cancellationToken);
            throw;
        }

        // Outside the try: a handler that throws here must not be told the run failed.
        scope.Publish(() => Completed(scope, awaitingApproval ? RunOutcome.AwaitingApproval : RunOutcome.Succeeded));
        return response;
    }

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ChatMessage[] input = [.. messages];
        RunScope scope = Start(input);
        bool awaitingApproval = false;
        // Set before any RunCompleted is published, so the finally below publishes one only for a stream that was left unfinished.
        bool completed = false;
        try
        {
            IAsyncEnumerator<AgentResponseUpdate> updates;
            try
            {
                updates = base.RunCoreStreamingAsync(input, session, options, cancellationToken).GetAsyncEnumerator(cancellationToken);
            }
            catch (Exception error)
            {
                completed = true;
                PublishFailure(scope, error, cancellationToken);
                throw;
            }

            await using (updates.ConfigureAwait(false))
            {
                while (true)
                {
                    AgentResponseUpdate update;
                    try
                    {
                        if (!await updates.MoveNextAsync().ConfigureAwait(false))
                        {
                                            break;
                        }
                        update = updates.Current;
                        foreach (ToolApprovalRequestContent request in update.Contents.OfType<ToolApprovalRequestContent>())
                        {
                            PublishApprovalRequest(scope, request);
                            awaitingApproval = true;
                        }
                    }
                    catch (Exception error)
                    {
                        completed = true;
                        PublishFailure(scope, error, cancellationToken);
                        throw;
                    }

                    yield return update;
                    // The iterator resumes on the consumer's async flow, which does not hold the scope.
                    RunScope.Current = scope;
                }
            }

            completed = true;
            scope.Publish(() => Completed(scope, awaitingApproval ? RunOutcome.AwaitingApproval : RunOutcome.Succeeded));
        }
        finally
        {
            // The consumer stopped reading before the stream ended: no failure, no cancelled token, no end of stream.
            if (!completed)
            {
                completed = true;
                scope.Publish(() => Completed(scope, RunOutcome.Abandoned));
            }
        }
    }

    private RunScope Start(IEnumerable<ChatMessage> input)
    {
        RunScope scope = new(Id, Name, publisher, RunScope.Current);
        RunScope.Current = scope;
        scope.Publish(() => new RunStarted());

        // A refused call never reaches the tool, so its only trace is the answer sent back in this run's input.
        foreach (ToolApprovalResponseContent response in input.SelectMany(message => message.Contents).OfType<ToolApprovalResponseContent>())
        {
            if (!response.Approved && response.ToolCall is FunctionCallContent call)
            {
                scope.Publish(() => new ToolCallCompleted(call.CallId, call.Name, ToolCallStatus.Rejected, response.Reason, null, TimeSpan.Zero));
            }
        }
        return scope;
    }

    private static void PublishApprovalRequest(RunScope scope, ToolApprovalRequestContent request)
    {
        if (request.ToolCall is FunctionCallContent call)
        {
            scope.Publish(() => new ToolApprovalRequested(call.CallId, call.Name, Arguments.Of(call.Arguments)));
        }
    }

    private static RunCompleted Completed(RunScope scope, RunOutcome outcome, Exception? error = null) =>
        new(outcome, error, scope.Elapsed, scope.Usage);

    private static void PublishFailure(RunScope scope, Exception error, CancellationToken cancellationToken)
    {
        RunOutcome outcome = error is OperationCanceledException && cancellationToken.IsCancellationRequested ? RunOutcome.Cancelled : RunOutcome.Failed;
        scope.Publish(() => Completed(scope, outcome, error));
    }
}
