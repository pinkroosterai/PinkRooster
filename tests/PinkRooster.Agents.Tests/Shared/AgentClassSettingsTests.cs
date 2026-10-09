using System.Reflection;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents.Prompts;
using PinkRooster.ToolCollections;
using PinkRooster.ToolCollections.Tests;

namespace PinkRooster.Agents.Tests.Shared;

public sealed class AgentClassSettingsTests
{
    private static ChatMessage Reply(string text) => new(ChatRole.Assistant, text);

    private static ChatMessage Call(string name) => new(ChatRole.Assistant, [new FunctionCallContent($"call-{name}", name, new Dictionary<string, object?>())]);

    [AgentRole("You describe yourself.")]
    [AgentDescription("Reviews pull requests.")]
    private sealed class Described(IChatClient chatClient) : DeclaredAgent(chatClient);

    [AgentRole("You describe yourself.")]
    [AgentDescription("Reviews pull requests.")]
    private class DescribedBase(IChatClient chatClient) : DeclaredAgent(chatClient);

    [AgentDescription("Reviews security.")]
    private sealed class DescribedDerived(IChatClient chatClient) : DescribedBase(chatClient);

    [AgentRole("You have a blank description.")]
    [AgentDescription(" ")]
    private sealed class BlankDescription(IChatClient chatClient) : DeclaredAgent(chatClient);

    [AgentRole("You have defaults.")]
    private sealed class WithDefaults(IChatClient chatClient) : DeclaredAgent(chatClient);

    [AgentRole("You have none.")]
    [AgentWithoutDefaults]
    private sealed class NoDefaults(IChatClient chatClient) : DeclaredAgent(chatClient);

    [AgentRole("You have none.")]
    [AgentWithoutDefaults]
    private sealed class DefaultsBackInConfigure(IChatClient chatClient) : DeclaredAgent(chatClient)
    {
        protected override void Configure(AgentBuilder agent) => agent.WithDefaults(AgentDefaults.BuiltIn);
    }

    [AgentRole("You loop.")]
    [AgentToolLoop(MaximumIterationsPerRequest = 4, IncludeDetailedErrors = true)]
    private class Looping(IChatClient chatClient) : DeclaredAgent(chatClient);

    [AgentToolLoop(MaximumIterationsPerRequest = 9)]
    private sealed class LoopingDerived(IChatClient chatClient) : Looping(chatClient);

    [AgentRole("You loop, then Configure changes it.")]
    [AgentToolLoop(MaximumIterationsPerRequest = 4)]
    private sealed class LoopReconfigured(IChatClient chatClient) : DeclaredAgent(chatClient)
    {
        protected override void Configure(AgentBuilder agent) => agent.ConfigureToolLoop(loop => loop.MaximumIterationsPerRequest = 6);
    }

    private sealed class Reading : ToolCollection
    {
        [Tool("Read", "Reads the file, and needs no approval of its own.")]
        public string Read() => "read";
    }

    [AgentRole("You ask first.")]
    [AgentTools(typeof(Reading))]
    [AgentRequireApproval("Read")]
    private sealed class Approving(IChatClient chatClient) : DeclaredAgent(chatClient);

    [AgentRole("You ask about a missing tool.")]
    [AgentRequireApproval("NoSuchTool")]
    private sealed class ApprovingNothing(IChatClient chatClient) : DeclaredAgent(chatClient);

    [AgentRole("You name no tool.")]
    [AgentRequireApproval]
    private sealed class NamingNoTool(IChatClient chatClient) : DeclaredAgent(chatClient);

    private static FunctionInvokingChatClient LoopOf(AIAgent agent)
    {
        PropertyInfo inner = typeof(DelegatingChatClient).GetProperty("InnerClient", BindingFlags.Instance | BindingFlags.NonPublic)!;
        IChatClient client = agent.GetService<ChatClientAgent>()!.ChatClient;
        while (client is not FunctionInvokingChatClient)
        {
            client = (IChatClient)inner.GetValue(client)!;
        }
        return (FunctionInvokingChatClient)client;
    }

    [Fact]
    public void AgentDescription_IsTheAgentsDescription()
    {
        Assert.Equal("Reviews pull requests.", new Described(new ScriptedChatClient(Reply("ok"))).Description);
    }

    [Fact]
    public void AgentDescription_OnTheDerivedClass_ReplacesTheBases()
    {
        Assert.Equal("Reviews security.", new DescribedDerived(new ScriptedChatClient(Reply("ok"))).Description);
    }

    [Fact]
    public void BlankAgentDescription_FailsNamingTheClass()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => new BlankDescription(new ScriptedChatClient(Reply("ok"))).GetService<ChatClientAgent>());

        Assert.Contains("BlankDescription", error.Message);
        Assert.Contains("[AgentDescription]", error.Message);
    }

    [Fact]
    public async Task AgentWithoutDefaults_LeavesTheDefaultsOutOfThePrompt()
    {
        ScriptedChatClient with = new(Reply("ok"));
        ScriptedChatClient without = new(Reply("ok"));

        await new WithDefaults(with).RunAsync("go");
        await new NoDefaults(without).RunAsync("go");

        string withPrompt = with.Options[0]!.Instructions!;
        string withoutPrompt = without.Options[0]!.Instructions!;
        Assert.True(withoutPrompt.Length < withPrompt.Length, "The prompt without defaults should be shorter.");
        Assert.Contains("You have none.", withoutPrompt);
    }

    [Fact]
    public async Task WithDefaultsInConfigure_WinsOverAgentWithoutDefaults()
    {
        ScriptedChatClient with = new(Reply("ok"));
        ScriptedChatClient back = new(Reply("ok"));

        await new WithDefaults(with).RunAsync("go");
        await new DefaultsBackInConfigure(back).RunAsync("go");

        Assert.Equal(with.Options[0]!.Instructions!.Replace("You have defaults.", "X"), back.Options[0]!.Instructions!.Replace("You have none.", "X"));
    }

    [Fact]
    public void AgentToolLoop_SetsOnlyWhatItNames()
    {
        FunctionInvokingChatClient loop = LoopOf(new Looping(new ScriptedChatClient(Reply("ok"))));

        Assert.Equal(4, loop.MaximumIterationsPerRequest);
        Assert.True(loop.IncludeDetailedErrors);
        Assert.False(loop.AllowConcurrentInvocation);
    }

    [Fact]
    public void AgentToolLoop_OnTheDerivedClass_ReplacesPropertyByProperty()
    {
        FunctionInvokingChatClient loop = LoopOf(new LoopingDerived(new ScriptedChatClient(Reply("ok"))));

        Assert.Equal(9, loop.MaximumIterationsPerRequest);
        Assert.True(loop.IncludeDetailedErrors);
    }

    [Fact]
    public void ConfigureToolLoop_InConfigure_RunsAfterTheAttribute()
    {
        Assert.Equal(6, LoopOf(new LoopReconfigured(new ScriptedChatClient(Reply("ok")))).MaximumIterationsPerRequest);
    }

    [Fact]
    public async Task AgentRequireApproval_MarksAToolOfANamedCollection()
    {
        AIAgent agent = new Approving(new ScriptedChatClient(Call("Read"), Reply("done")));

        AgentResponse asked = await agent.RunAsync("go");

        Assert.Single(asked.Messages.SelectMany(message => message.Contents).OfType<ToolApprovalRequestContent>());
    }

    [Fact]
    public void AgentRequireApproval_NamingAMissingTool_FailsTheBuild()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => new ApprovingNothing(new ScriptedChatClient(Reply("ok"))).GetService<ChatClientAgent>());

        Assert.Contains("ApprovingNothing", error.Message);
        Assert.Contains("NoSuchTool", error.Message);
    }

    [Fact]
    public void AgentRequireApproval_NamingNoTool_FailsNamingTheClass()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => new NamingNoTool(new ScriptedChatClient(Reply("ok"))).GetService<ChatClientAgent>());

        Assert.Contains("NamingNoTool", error.Message);
        Assert.Contains("[AgentRequireApproval]", error.Message);
    }
}
