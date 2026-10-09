using System.Diagnostics;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace PinkRooster.Agents.Eventing;

/// <summary>MAF function-invocation middleware that publishes the start and end of every tool call that runs.</summary>
internal static class ToolCallEvents
{
    public static async ValueTask<object?> InvokeAsync(
        AIAgent agent,
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next,
        CancellationToken cancellationToken)
    {
        if (RunScope.Current is not RunScope scope)
        {
            return await next(context, cancellationToken).ConfigureAwait(false);
        }

        string callId = context.CallContent.CallId;
        string name = context.Function.Name;
        scope.Publish(() => new ToolCallStarted(callId, name, Arguments.Of(context.Arguments)));
        long started = Stopwatch.GetTimestamp();
        object? result;
        try
        {
            result = await next(context, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            TimeSpan failedAfter = Stopwatch.GetElapsedTime(started);
            ToolCallStatus status = error is OperationCanceledException && cancellationToken.IsCancellationRequested ? ToolCallStatus.Cancelled : ToolCallStatus.Failed;
            scope.Publish(() => new ToolCallCompleted(callId, name, status, null, error, failedAfter));
            throw;
        }

        // Published outside the try, so the success completion is never a second one after a failure.
        TimeSpan duration = Stopwatch.GetElapsedTime(started);
        scope.Publish(() => new ToolCallCompleted(callId, name, ToolCallStatus.Succeeded, result, null, duration));
        return result;
    }
}
