using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.ToolCollections;
using PinkRooster.ToolCollections.Tests;

namespace PinkRooster.Agents.Tests.Tools;

public sealed class AgentClassCollectionsTests
{
    private static ChatMessage Reply(string text) => new(ChatRole.Assistant, text);

    private sealed class Alpha : ToolCollection
    {
        [Tool("Alpha", "First collection's tool.")]
        public string Run() => "alpha";
    }

    private sealed class Beta : ToolCollection
    {
        [Tool("Beta", "Second collection's tool.")]
        public string Run() => "beta";
    }

    private sealed class NeedsArgument(string root) : ToolCollection
    {
        [Tool("Needs", "Takes a constructor argument.")]
        public string Run() => root;
    }

    [AgentRole("You use collections.")]
    [AgentTools(typeof(Alpha), typeof(Beta))]
    private sealed class Named(IChatClient chatClient) : DeclaredAgent(chatClient)
    {
        [Tool("Own", "The class's own tool.")]
        public string Own() => "own";

        protected override void Configure(AgentBuilder agent) => agent.WithTool(AIFunctionFactory.Create(() => "late", "Late", "Added in Configure."));
    }

    [AgentRole("You hide a tool.")]
    private class HidesATool(IChatClient chatClient) : DeclaredAgent(chatClient)
    {
        [Tool("Hidden", "Marked, but protected.")]
        protected string Hidden() => "hidden";
    }

    [Fact]
    public void AnAgentClass_WithAToolAttributeOnANonPublicMethod_FailsToBuild_NamingTheMethodAndTheFix()
    {
        HidesATool agent = new(new ScriptedChatClient(Reply("done")));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => agent.GetService<ChatClientAgent>());

        Assert.Contains("'HidesATool.Hidden'", error.Message);
        Assert.Contains("protected", error.Message);
        Assert.Contains("Make the method public", error.Message);
    }

    [AgentTools(typeof(Alpha))]
    private abstract class BaseAgent(IChatClient chatClient) : DeclaredAgent(chatClient);

    [AgentRole("You inherit.")]
    [AgentTools(typeof(Beta))]
    private sealed class Derived(IChatClient chatClient) : BaseAgent(chatClient);

    [AgentRole("You name no collection.")]
    [AgentTools]
    private sealed class Empty(IChatClient chatClient) : DeclaredAgent(chatClient);

    [AgentRole("You name a plain type.")]
    [AgentTools(typeof(string))]
    private sealed class NotACollection(IChatClient chatClient) : DeclaredAgent(chatClient);

    [AgentRole("You name a collection that needs arguments.")]
    [AgentTools(typeof(NeedsArgument))]
    private sealed class NeedsArguments(IChatClient chatClient) : DeclaredAgent(chatClient);

    [Fact]
    public async Task NamedCollections_AreOffered_AfterTheOwnToolsAndBeforeConfigure()
    {
        ScriptedChatClient client = new(Reply("ok"));

        await new Named(client).RunAsync("go");

        Assert.Equal(["Late", "Own", "Alpha", "Beta"], client.Options[0]!.Tools!.Select(tool => tool.Name));
    }

    [Fact]
    public async Task NamedCollections_OnABase_ComeBeforeTheDerivedClassesOwn()
    {
        ScriptedChatClient client = new(Reply("ok"));

        await new Derived(client).RunAsync("go");

        Assert.Equal(["Alpha", "Beta"], client.Options[0]!.Tools!.Select(tool => tool.Name));
    }

    [Fact]
    public async Task EachInstance_GetsItsOwnCollectionObjects()
    {
        ScriptedChatClient client = new(Reply("ok"), Reply("ok"));
        Named first = new(client);
        Named second = new(client);

        await first.RunAsync("go");
        await second.RunAsync("go");

        Assert.Equal(client.Options[0]!.Tools!.Select(tool => tool.Name), client.Options[1]!.Tools!.Select(tool => tool.Name));
        Assert.NotSame(client.Options[0]!.Tools![2], client.Options[1]!.Tools![2]);
    }

    [Fact]
    public void AsAgentBuilder_HoldsTheNamedCollections()
    {
        AIAgent agent = new Named(new ScriptedChatClient(Reply("ok"))).AsAgentBuilder().Build();

        Assert.NotNull(agent.GetService<ChatClientAgent>());
    }

    [Theory]
    [InlineData(typeof(Empty), "names no tool collection")]
    [InlineData(typeof(NotACollection), "not a concrete ToolCollection")]
    [InlineData(typeof(NeedsArguments), "no public parameterless constructor")]
    public void ABadAttribute_FailsNamingTheClassAndTheFix(Type agentType, string reason)
    {
        DeclaredAgent agent = (DeclaredAgent)Activator.CreateInstance(agentType, new ScriptedChatClient(Reply("ok")))!;

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => agent.GetService<ChatClientAgent>());

        Assert.Contains(agentType.Name, error.Message);
        Assert.Contains(reason, error.Message);
    }
}
