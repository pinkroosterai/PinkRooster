using System.Reflection;
using Microsoft.Extensions.AI;

namespace PinkRooster.Agents;

/// <summary>Starts an <see cref="AgentBuilder"/> from a chat client.</summary>
public static class AgentBuilderChatClientExtensions
{
    /// <summary>Starts composing an agent on this client. Call <see cref="AgentBuilder.Build"/> to get the agent.</summary>
    /// <example>
    /// <code>
    /// AIAgent agent = chatClient
    ///     .CreateAgent()
    ///     .WithRole("You are a build assistant.")
    ///     .WithTools(new ShellToolCollection())
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder CreateAgent(this IChatClient chatClient) => new(chatClient);

    /// <summary>Creates an agent class on this client, through its public constructor that takes only the <see cref="IChatClient"/>.</summary>
    /// <typeparam name="TAgent">An agent class, such as <c>ReviewerAgent</c>. A class whose constructor takes more than the client is created with <c>new</c>.</typeparam>
    /// <exception cref="ArgumentNullException">The client is null.</exception>
    /// <exception cref="InvalidOperationException">The class has no public constructor taking only an <see cref="IChatClient"/>; the message names the fix.</exception>
    /// <example>
    /// <code>
    /// await using ReviewerAgent reviewer = chatClient.CreateAgent&lt;ReviewerAgent&gt;();
    /// </code>
    /// </example>
    public static TAgent CreateAgent<TAgent>(this IChatClient chatClient) where TAgent : DeclaredAgent
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ConstructorInfo constructor = typeof(TAgent).GetConstructor(BindingFlags.Public | BindingFlags.Instance, [typeof(IChatClient)])
            ?? throw new InvalidOperationException(
                $"{typeof(TAgent).Name} has no public constructor that takes only an {nameof(IChatClient)}, so CreateAgent<{typeof(TAgent).Name}>() cannot create it. " +
                $"Add one, or create the agent with new {typeof(TAgent).Name}(...).");
        return (TAgent)constructor.Invoke(BindingFlags.DoNotWrapExceptions, binder: null, [chatClient], culture: null);
    }

    /// <summary>Creates an agent class as <see cref="CreateAgent{TAgent}(IChatClient)"/> does, then applies <paramref name="configure"/> to its builder after the class's own <c>Configure</c>.</summary>
    /// <remarks>The result is still the <typeparamref name="TAgent"/> instance, with its own tools and event handlers. The callback runs when the agent is first used, so a mistake in it surfaces there.</remarks>
    /// <typeparam name="TAgent">An agent class, such as <c>ReviewerAgent</c>.</typeparam>
    /// <param name="chatClient">The model client the agent uses.</param>
    /// <param name="configure">The change, such as <c>builder => builder.WithInstruction("Keep it short.")</c>. It may overwrite what the class set.</param>
    /// <exception cref="ArgumentNullException">The client or the callback is null.</exception>
    /// <example>
    /// <code>
    /// await using ReviewerAgent reviewer = chatClient.CreateAgent&lt;ReviewerAgent&gt;(builder => builder.WithInstruction("Keep it short."));
    /// </code>
    /// </example>
    public static TAgent CreateAgent<TAgent>(this IChatClient chatClient, Action<AgentBuilder> configure) where TAgent : DeclaredAgent
    {
        ArgumentNullException.ThrowIfNull(configure);
        TAgent agent = CreateAgent<TAgent>(chatClient);
        agent.AddBuilderCallback(configure);
        return agent;
    }
}
