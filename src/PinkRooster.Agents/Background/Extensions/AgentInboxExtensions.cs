using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents.Background;

// Declared in the package's root namespace by hand, like CreateAgent: a caller finds it with the root using alone.
namespace PinkRooster.Agents;

/// <summary>Posting a message to an agent while it runs.</summary>
public static class AgentInboxExtensions
{
    /// <summary>
    /// Puts <paramref name="message"/>, as a user message, in the inbox of the run in progress on <paramref name="session"/>. The model reads it before
    /// its next model call; a model that had ended its turn while tasks run is called again for it, and a waiting <c>WaitForTasks</c> returns.
    /// </summary>
    /// <remarks>
    /// Safe to call from any thread while the run goes on. The model reads the message as part of the conversation; for another
    /// role or an author name, post a <see cref="ChatMessage"/>.
    /// </remarks>
    /// <param name="agent">An agent built by <see cref="AgentBuilder"/> with <see cref="AgentBuilder.WithInbox"/> or <see cref="AgentBuilder.AllowBackground"/>.</param>
    /// <param name="session">The session the run is on.</param>
    /// <param name="message">The text to post; it must not be blank.</param>
    /// <param name="cancellationToken">Cancels the posting.</param>
    /// <exception cref="InvalidOperationException">The agent has no inbox, or no run is in progress on the session; the message names the fix.</exception>
    public static Task PostMessageAsync(this AIAgent agent, AgentSession session, string message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return PostMessageAsync(agent, session, new ChatMessage(ChatRole.User, message), cancellationToken);
    }

    /// <summary>
    /// Puts <paramref name="message"/> in the inbox of the run in progress on <paramref name="session"/>, in the role and with the author it carries.
    /// The rules are those of <see cref="PostMessageAsync(AIAgent, AgentSession, string, CancellationToken)"/>.
    /// </summary>
    /// <param name="agent">An agent built by <see cref="AgentBuilder"/> with <see cref="AgentBuilder.WithInbox"/> or <see cref="AgentBuilder.AllowBackground"/>.</param>
    /// <param name="session">The session the run is on.</param>
    /// <param name="message">The message to post.</param>
    /// <param name="cancellationToken">Cancels the posting.</param>
    /// <exception cref="InvalidOperationException">The agent has no inbox, or no run is in progress on the session; the message names the fix.</exception>
    public static async Task PostMessageAsync(this AIAgent agent, AgentSession session, ChatMessage message, CancellationToken cancellationToken = default)
    {
        if (!await agent.TryPostMessageAsync(session, message, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "No run is in progress on this session, so there is nobody to read the message. Post while RunAsync or RunStreamingAsync is running, " +
                $"or pass the message as the input of the next run. {nameof(TryPostMessageAsync)} returns false instead of throwing.");
        }
    }

    /// <summary>Whether the agent has an inbox: it was built with <see cref="AgentBuilder.WithInbox"/> or <see cref="AgentBuilder.AllowBackground"/>.</summary>
    /// <param name="agent">The agent to ask.</param>
    public static bool HasInbox(this AIAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return agent.GetService<BackgroundRunAgent>() is not null;
    }

    /// <summary>
    /// As <see cref="PostMessageAsync(AIAgent, AgentSession, string, CancellationToken)"/>, but returns false instead of throwing when the
    /// run in progress on <paramref name="session"/> cannot read the message: no run is in progress, or the run is ending. For a
    /// host that keeps what it could not post, such as a console that takes typed lines.
    /// </summary>
    /// <remarks>
    /// True means the message is in the session's inbox. The run in progress reads it before its next model call; while a run waits
    /// for an approval, the run that carries the answers reads it. If the run is cancelled or fails first, the message stays in the
    /// session and its next run reads it.
    /// </remarks>
    /// <param name="agent">An agent built by <see cref="AgentBuilder"/> with <see cref="AgentBuilder.WithInbox"/> or <see cref="AgentBuilder.AllowBackground"/>.</param>
    /// <param name="session">The session the run is on.</param>
    /// <param name="message">The text to post; it must not be blank.</param>
    /// <param name="cancellationToken">Cancels the posting.</param>
    /// <exception cref="InvalidOperationException">The agent has no inbox; the message names the fix.</exception>
    public static Task<bool> TryPostMessageAsync(this AIAgent agent, AgentSession session, string message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return TryPostMessageAsync(agent, session, new ChatMessage(ChatRole.User, message), cancellationToken);
    }

    /// <summary>As <see cref="TryPostMessageAsync(AIAgent, AgentSession, string, CancellationToken)"/>, for a message in the role and with the author it carries.</summary>
    /// <param name="agent">An agent built by <see cref="AgentBuilder"/> with <see cref="AgentBuilder.WithInbox"/> or <see cref="AgentBuilder.AllowBackground"/>.</param>
    /// <param name="session">The session the run is on.</param>
    /// <param name="message">The message to post.</param>
    /// <param name="cancellationToken">Cancels the posting.</param>
    /// <exception cref="InvalidOperationException">The agent has no inbox; the message names the fix.</exception>
    public static async Task<bool> TryPostMessageAsync(this AIAgent agent, AgentSession session, ChatMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(message);

        if (agent.GetService<BackgroundRunAgent>() is not BackgroundRunAgent inbox)
        {
            throw new InvalidOperationException(
                $"This agent has no inbox. Build it with {nameof(AgentBuilder)}.{nameof(AgentBuilder.WithInbox)}(), or with {nameof(AgentBuilder.AllowBackground)}, which gives it one too.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        return inbox.Find(session) is BackgroundTaskStore store && await store.TryPostFromHostAsync(message).ConfigureAwait(false);
    }
}
