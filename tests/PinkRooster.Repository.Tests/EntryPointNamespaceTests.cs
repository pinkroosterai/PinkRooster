using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents;
using PinkRooster.ToolCollections;

namespace PinkRooster.Repository.Tests;

/// <summary>
/// The front door resolves with the packages' root namespaces alone: this file has no other <c>PinkRooster.Agents</c> or
/// <c>PinkRooster.ToolCollections</c> using.
/// </summary>
public sealed class EntryPointNamespaceTests
{
    private sealed class NoClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    // One attribute from each folder that holds agent-class attributes, and a [Tool] method.
    [AgentRole("You review.")]
    [AgentWithoutDefaults]
    [AgentRequireApproval("Check")]
    [AgentAllowBackground("Check")]
    private sealed class Reviewer(IChatClient chatClient) : DeclaredAgent(chatClient)
    {
        [Tool("Check", "Checks it.")]
        public string Check() => "checked";
    }

    [Fact]
    public void AnAgentClass_WithItsAttributesAndAToolMethod_ResolvesWithOnlyTheRootNamespaces()
    {
        Reviewer agent = new NoClient().CreateAgent<Reviewer>();

        Assert.Equal(nameof(Reviewer), agent.Name);
    }

    [Fact]
    public void CreateAgent_ResolvesWithOnlyTheRootNamespace()
    {
        AIAgent agent = new NoClient().CreateAgent().WithRole("r").Build();

        Assert.NotNull(agent);
    }
}
