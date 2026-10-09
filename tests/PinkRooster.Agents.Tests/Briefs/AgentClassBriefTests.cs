using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;


namespace PinkRooster.Agents.Tests.Briefs;

public sealed class AgentClassBriefTests
{
    private static ChatMessage Reply(string text = "ok") => new(ChatRole.Assistant, text);

    [AgentRole("You review pull requests.")]
    [AgentObjective("Find bugs before they merge.")]
    [AgentBackground("The repo is a .NET library.", "Warnings are errors.")]
    [AgentInstruction("Quote the line you mean.")]
    [AgentInstruction("Say it once.", "Then stop.")]
    [AgentConstraint("Never approve a change you have not read.")]
    [AgentOutputFormat("A numbered list, most severe first.")]
    [AgentExample("Review #12", "1. A null check is missing.")]
    private sealed class Reviewer(IChatClient chatClient) : DeclaredAgent(chatClient);

    [AgentRole("Base role.")]
    [AgentObjective("Base objective.")]
    [AgentInstruction("Base instruction.")]
    [AgentConstraint("Base constraint.")]
    [AgentExample("base in", "base out")]
    private abstract class BaseReviewer(IChatClient chatClient) : DeclaredAgent(chatClient);

    [AgentRole("Derived role.")]
    [AgentInstruction("Derived instruction.")]
    [AgentConstraint("Derived constraint.")]
    [AgentExample("derived in", "derived out")]
    private sealed class DerivedReviewer(IChatClient chatClient) : BaseReviewer(chatClient);

    [AgentRole("Attribute role.")]
    [AgentInstruction("From the attribute.")]
    private sealed class Reconfigured(IChatClient chatClient) : DeclaredAgent(chatClient)
    {
        protected override void Configure(AgentBuilder agent) => agent.WithRole("Configured role.").WithInstruction("From Configure.");
    }

    [AgentRole("Has a role.")]
    private sealed class WithSystemPromptInConfigure(IChatClient chatClient) : DeclaredAgent(chatClient)
    {
        protected override void Configure(AgentBuilder agent) => agent.WithSystemPrompt("Whole prompt.");
    }

    private sealed class NoRole(IChatClient chatClient) : DeclaredAgent(chatClient);

    [AgentRole("Role set in Configure only.")]
    private sealed class RoleFromConfigure(IChatClient chatClient) : DeclaredAgent(chatClient)
    {
        protected override void Configure(AgentBuilder agent) => agent.WithName("renamed");
    }

    [AgentRole("  ")]
    private sealed class BlankRole(IChatClient chatClient) : DeclaredAgent(chatClient);

    [AgentRole("Role.")]
    [AgentInstruction("fine", " ")]
    private sealed class BlankInstruction(IChatClient chatClient) : DeclaredAgent(chatClient);

    [AgentRole("Role.")]
    [AgentExample("in", "")]
    private sealed class BlankExample(IChatClient chatClient) : DeclaredAgent(chatClient);

    [AgentRole("Role.")]
    private sealed class ThrowingConfigure(IChatClient chatClient) : DeclaredAgent(chatClient)
    {
        public int Builds { get; private set; }

        protected override void Configure(AgentBuilder agent)
        {
            Builds++;
            agent.RequireApproval("NoSuchTool");
        }
    }

    [Fact]
    public async Task AgentClassWithEveryAttribute_SendsTheSamePromptAsABuilderChainWithTheSameText()
    {
        ScriptedChatClient classClient = new(Reply());
        ScriptedChatClient builderClient = new(Reply());
        AIAgent byClass = new Reviewer(classClient);
        AIAgent byBuilder = builderClient.CreateAgent()
            .WithRole("You review pull requests.")
            .WithObjective("Find bugs before they merge.")
            .WithBackground("The repo is a .NET library.\nWarnings are errors.")
            .WithInstructions(["Quote the line you mean.", "Say it once.", "Then stop."])
            .WithConstraint("Never approve a change you have not read.")
            .WithOutputFormat("A numbered list, most severe first.")
            .WithExample("Review #12", "1. A null check is missing.")
            .Build();

        await byClass.RunAsync("go");
        await byBuilder.RunAsync("go");

        string? prompt = classClient.Options[0]?.Instructions?.ReplaceLineEndings("\n");
        Assert.Contains("# Role\nYou review pull requests.", prompt);
        Assert.Contains("# Background\nThe repo is a .NET library.\nWarnings are errors.", prompt);
        Assert.Equal(builderClient.Options[0]?.Instructions?.ReplaceLineEndings("\n"), prompt);
    }

    [Fact]
    public async Task DerivedClass_ReplacesSingleTextSections_AndListsTheBaseTypesLinesFirst()
    {
        ScriptedChatClient client = new(Reply());

        await new DerivedReviewer(client).RunAsync("go");

        string prompt = client.Options[0]!.Instructions!.ReplaceLineEndings("\n");
        Assert.Contains("# Role\nDerived role.", prompt);
        Assert.DoesNotContain("Base role.", prompt);
        Assert.Contains("# Objective\nBase objective.", prompt);
        Assert.True(prompt.IndexOf("Base instruction.", StringComparison.Ordinal) < prompt.IndexOf("Derived instruction.", StringComparison.Ordinal));
        Assert.True(prompt.IndexOf("Base constraint.", StringComparison.Ordinal) < prompt.IndexOf("Derived constraint.", StringComparison.Ordinal));
        Assert.True(prompt.IndexOf("base in", StringComparison.Ordinal) < prompt.IndexOf("derived in", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Configure_ReplacesASingleTextSectionOfTheAttribute_AndAddsItsLinesAfterTheAttributes()
    {
        ScriptedChatClient client = new(Reply());

        await new Reconfigured(client).RunAsync("go");

        string prompt = client.Options[0]!.Instructions!.ReplaceLineEndings("\n");
        Assert.Contains("# Role\nConfigured role.", prompt);
        Assert.DoesNotContain("Attribute role.", prompt);
        Assert.True(prompt.IndexOf("From the attribute.", StringComparison.Ordinal) < prompt.IndexOf("From Configure.", StringComparison.Ordinal));
    }

    [Fact]
    public void ClassWithoutARole_FailsAtGetService_NamingTheClassAndAgentRole_WithoutAModelCall()
    {
        ScriptedChatClient client = new(Reply());
        AIAgent agent = new NoRole(client);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => agent.GetService<ChatClientAgent>());

        Assert.Contains("'NoRole'", error.Message);
        Assert.Contains("[AgentRole", error.Message);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task ClassWhoseConfigureSetsTheRole_Runs()
    {
        ScriptedChatClient client = new(Reply());
        AIAgent agent = new RoleFromConfigure(client);

        await agent.RunAsync("go");

        Assert.Equal("renamed", agent.Name);
    }

    [Fact]
    public async Task ClassWithoutARole_FailsTheFirstRun_NotTheConstruction()
    {
        AIAgent agent = new NoRole(new ScriptedChatClient(Reply()));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => agent.RunAsync("go"));

        Assert.Contains("[AgentRole", error.Message);
    }

    [Theory]
    [InlineData(typeof(BlankRole), "[AgentRole]")]
    [InlineData(typeof(BlankInstruction), "[AgentInstruction]")]
    [InlineData(typeof(BlankExample), "[AgentExample]")]
    public void BlankAttributeText_FailsNamingTheClassTheAttributeAndTheFix(Type type, string attribute)
    {
        AIAgent agent = (AIAgent)Activator.CreateInstance(type, new ScriptedChatClient(Reply()))!;

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => agent.GetService<ChatClientAgent>());

        Assert.Contains($"'{type.Name}'", error.Message);
        Assert.Contains(attribute, error.Message);
        Assert.Contains("give it text, or remove the attribute", error.Message);
    }

    [Fact]
    public void AMistakeInConfigure_FailsNamingTheClassWithTheBuildersMessage_AndIsNotKept()
    {
        ThrowingConfigure agent = new(new ScriptedChatClient(Reply()));

        InvalidOperationException first = Assert.Throws<InvalidOperationException>(() => agent.GetService<ChatClientAgent>());
        InvalidOperationException second = Assert.Throws<InvalidOperationException>(() => agent.GetService<ChatClientAgent>());

        Assert.Contains("'ThrowingConfigure'", first.Message);
        Assert.Contains("'NoSuchTool'", first.Message);
        Assert.Contains("Its tools are", first.Message);
        Assert.Equal(2, agent.Builds);
        Assert.Equal(first.Message, second.Message);
    }

    [Fact]
    public void ASystemPromptInConfigureOnAClassWithAttributes_FailsNamingTheClassAsTheBrief()
    {
        AIAgent agent = new WithSystemPromptInConfigure(new ScriptedChatClient(Reply()));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => agent.GetService<ChatClientAgent>());

        Assert.Contains("'WithSystemPromptInConfigure'", error.Message);
        Assert.Contains("cannot be combined with section methods", error.Message);
    }

    [Fact]
    public void ANullChatClient_FailsAtConstruction()
    {
        Assert.Throws<ArgumentNullException>(() => new Reviewer(null!));
    }

    [Fact]
    public void TheAgentsName_IsTheClassNameUnlessConfigureSetsAnother()
    {
        AIAgent plain = new Reviewer(new ScriptedChatClient(Reply()));
        AIAgent renamed = new RoleFromConfigure(new ScriptedChatClient(Reply()));

        Assert.Equal("Reviewer", plain.Name);
        Assert.Equal("renamed", renamed.Name);
    }
}
