using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents.Eventing;

namespace PinkRooster.Agents.Background;

/// <summary>
/// MAF's loop evaluator for the wake: after the model ends its turn it waits, without a model call, while tasks run and the inbox
/// is empty; it has the model called again when a task ended or a message was posted; and it ends the run when neither remains,
/// or when the limit on wakes is reached.
/// </summary>
internal sealed class BackgroundWake : LoopEvaluator
{
    public override async ValueTask<LoopEvaluation> EvaluateAsync(LoopContext context, CancellationToken cancellationToken = default)
    {
        AgentSession session = context.Session
            ?? throw new InvalidOperationException("The wake ran without a session; BackgroundRunAgent always supplies one.");
        BackgroundTaskStore store = BackgroundTaskStore.Current
            ?? throw new InvalidOperationException("The wake ran outside a run; BackgroundRunAgent always opens one.");
        MessageInjectingChatClient? inbox = context.Agent.GetService<MessageInjectingChatClient>();

        // A run that asks for an approval is paused: its tasks go on, and the answering run takes them over.
        if (context.LastResponse.Messages.SelectMany(message => message.Contents).OfType<ToolApprovalRequestContent>().Any())
        {
            return LoopEvaluation.Stop();
        }

        bool announced = false;
        while (true)
        {
            // Taken before the state is read, so a change between the reading and the waiting is not missed.
            Task change = store.NextChange();
            long hostPosts = store.HostPosts;
            if (inbox is not null && (await inbox.GetPendingMessagesAsync(session, cancellationToken).ConfigureAwait(false)).Count > 0)
            {
                if (!store.TryCountWake())
                {
                    int[] cancelled = store.RunningTaskIds();
                    store.LogWakeLimit(cancelled);
                    RunScope.Current?.Publish(() => new BackgroundWakeLimitReached(store.Options.MaxWakes, cancelled));
                    store.CloseInbox();
                    return LoopEvaluation.Stop();
                }
                // The inbox's messages reach the model with the call this starts; the wake adds none of its own.
                return LoopEvaluation.ContinueWithMessages([]);
            }

            int[] running = store.RunningTaskIds();
            // With no wake allowed there is nothing to wait for: the tasks could only end the run.
            if (running.Length == 0 || store.Options.MaxWakes == 0)
            {
                // The run ends here, so from here on the host is told that its message cannot be read. A message that came in
                // since the inbox was looked at is read first.
                if (!store.TryCloseInbox(hostPosts))
                {
                    await Task.Yield();
                    continue;
                }
                return LoopEvaluation.Stop();
            }
            if (!announced)
            {
                announced = true;
                RunScope.Current?.Publish(() => new BackgroundWaitStarted(running));
            }
            await change.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
