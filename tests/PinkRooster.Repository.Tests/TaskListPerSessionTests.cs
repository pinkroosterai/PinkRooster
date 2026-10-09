using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents;
using PinkRooster.Agents.Sessions;
using PinkRooster.Agents.Tests.TestSupport;
using PinkRooster.ToolCollections.BuiltIn.TaskLists;

namespace PinkRooster.Repository.Tests;

/// <summary>The task list's per-session mode on a real agent: one collection, a list per session, saved and restored with it.</summary>
public sealed class TaskListPerSessionTests
{
    private static ChatMessage AddTask(string callId, string subject) =>
        new(ChatRole.Assistant, [new FunctionCallContent(callId, "TaskAdd", new Dictionary<string, object?>
        {
            ["tasks"] = new[] { new Dictionary<string, object?> { ["subject"] = subject } }
        })]);

    private static ChatMessage Reply(string text) => new(ChatRole.Assistant, text);

    private static string Context(List<ChatMessage> request) =>
        request.LastOrDefault(message => message.Text.StartsWith("The current state of your tools", StringComparison.Ordinal))?.Text ?? "";

    [Fact]
    public async Task TwoSessionsOnOneCollection_EachKeepItsOwnList()
    {
        ScriptedChatClient model = new(AddTask("c1", "Write the tests"), Reply("ok"), AddTask("c2", "Ship it"), Reply("ok"), Reply("first again"));
        AIAgent agent = model.CreateAgent().WithRole("r").WithTools(new TaskListToolCollectionBuilder().PerSession().Build()).Build();
        AgentSession first = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
        AgentSession second = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        await agent.RunAsync("plan", first, cancellationToken: TestContext.Current.CancellationToken);
        await agent.RunAsync("plan", second, cancellationToken: TestContext.Current.CancellationToken);
        await agent.RunAsync("go on", first, cancellationToken: TestContext.Current.CancellationToken);

        // Requests 2 and 3 belong to the second session's run, request 4 to the first session's second run.
        Assert.DoesNotContain("Write the tests", Context(model.Requests[3]));
        Assert.Contains("#1 [pending] Ship it", Context(model.Requests[3]));
        Assert.Contains("#1 [pending] Write the tests", Context(model.Requests[4]));
        Assert.DoesNotContain("Ship it", Context(model.Requests[4]));
    }

    [Fact]
    public async Task GetTasks_ReturnsACopyOfOneSessionsList_AndThrowsNamingTheFixForASharedList()
    {
        ScriptedChatClient model = new(AddTask("c1", "Write the tests"), Reply("ok"));
        TaskListToolCollection tasks = new TaskListToolCollectionBuilder().PerSession().Build();
        AIAgent agent = model.CreateAgent().WithRole("r").WithTools(tasks).Build();
        AgentSession used = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
        AgentSession unused = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
        await agent.RunAsync("plan", used, cancellationToken: TestContext.Current.CancellationToken);

        TaskListSnapshot list = tasks.GetTasks(used);
        list.Items.Clear();

        Assert.Equal(["Write the tests"], tasks.GetTasks(used).Items.Select(item => item.Subject));
        Assert.Empty(tasks.GetTasks(unused).Items);
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => new TaskListToolCollection().GetTasks(used));
        Assert.Contains("PerSession()", error.Message);
    }

    [Fact]
    public async Task AList_IsSavedAndRestoredWithItsSession()
    {
        ScriptedChatClient model = new(AddTask("c1", "Write the tests"), Reply("ok"), Reply("restored"));
        AIAgent agent = model.CreateAgent().WithRole("r").WithTools(new TaskListToolCollectionBuilder().PerSession().Build()).Build();
        AgentSession session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
        await agent.RunAsync("plan", session, cancellationToken: TestContext.Current.CancellationToken);

        JsonElement saved = await agent.SerializeSessionAsync(session, cancellationToken: TestContext.Current.CancellationToken);
        AgentSession restored = await agent.DeserializeSessionAsync(saved, cancellationToken: TestContext.Current.CancellationToken);
        await agent.RunAsync("go on", restored, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("#1 [pending] Write the tests", Context(model.Requests[2]));
    }

    [Fact]
    public async Task AList_ComesBackWithASessionReadFromTheSessionStore_ByAnotherAgentInstance()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"tasklist-store-test-{Guid.NewGuid():N}");
        try
        {
            SessionStore store = new(directory);
            AIAgent agent = new ScriptedChatClient(AddTask("c1", "Write the tests"), Reply("ok"))
                .CreateAgent().WithRole("r").WithTools(new TaskListToolCollectionBuilder().PerSession().Build()).Build();
            AgentSession session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
            await agent.RunAsync("plan", session, cancellationToken: TestContext.Current.CancellationToken);
            SavedSession saved = await store.SaveAsync(agent, session, "plan", TestContext.Current.CancellationToken);

            ScriptedChatClient later = new(Reply("restored"));
            TaskListToolCollection laterTasks = new TaskListToolCollectionBuilder().PerSession().Build();
            AIAgent second = later.CreateAgent().WithRole("r").WithTools(laterTasks).Build();
            RestoredSession restored = await store.RestoreAsync(second, saved.Id, TestContext.Current.CancellationToken);
            await second.RunAsync("go on", restored.Session, cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(restored.SameTools);
            Assert.Contains("TaskAdd", saved.Tools);
            // The host reads the restored list before any model call.
            Assert.Equal(["Write the tests"], laterTasks.GetTasks(restored.Session).Items.Select(item => item.Subject));
            Assert.Contains("#1 [pending] Write the tests", Context(later.Requests[0]));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
