using Microsoft.Extensions.AI;
using PinkRooster.Agents.Tests.TestSupport;

namespace PinkRooster.Agents.Tests;

public sealed class AgentBuilderChatClientExtensionsTests
{
    [AgentRole("You answer.")]
    private sealed class Plain(IChatClient chatClient) : DeclaredAgent(chatClient);

    [AgentRole("You need a path.")]
    private sealed class NeedsPath(IChatClient chatClient, string path) : DeclaredAgent(chatClient)
    {
        public string Path { get; } = path;
    }

    [Fact]
    public async Task CreateAgentOfT_CreatesTheClass_AndItAnswers()
    {
        ScriptedChatClient client = new(new ChatMessage(ChatRole.Assistant, "hello"));

        Plain agent = client.CreateAgent<Plain>();

        Assert.Equal("hello", (await agent.RunAsync("hi")).Text);
    }

    [Fact]
    public async Task CreateAgentOfT_WithConfigure_ReturnsTheClassWithTheChangeApplied()
    {
        RecordingChatClient client = new(_ => "hello");

        Plain agent = client.CreateAgent<Plain>(builder => builder.WithInstruction("Keep it short."));
        await agent.RunAsync("hi");

        Assert.Contains("You answer.", client.Requests[0].Instructions);
        Assert.Contains("Keep it short.", client.Requests[0].Instructions);
    }

    [Fact]
    public void CreateAgentOfT_WithConfigure_ThrowsOnANullCallback()
    {
        RecordingChatClient client = new(_ => "x");

        Assert.Throws<ArgumentNullException>(() => client.CreateAgent<Plain>(null!));
    }

    [Fact]
    public void CreateAgentOfT_ForAClassThatNeedsMoreThanTheClient_ThrowsAndNamesTheFix()
    {
        ScriptedChatClient client = new(new ChatMessage(ChatRole.Assistant, "x"));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => client.CreateAgent<NeedsPath>());

        Assert.Contains("NeedsPath", error.Message);
        Assert.Contains("new NeedsPath(...)", error.Message);
    }
}
