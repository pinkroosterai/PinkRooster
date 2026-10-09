using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents.Sessions;
using PinkRooster.ToolCollections;

namespace PinkRooster.Agents.Tests.Sessions;

public sealed class SessionStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"sessionstore-test-{Guid.NewGuid():N}", "sessions");

    public void Dispose()
    {
        if (Directory.Exists(Path.GetDirectoryName(directory)))
        {
            Directory.Delete(Path.GetDirectoryName(directory)!, recursive: true);
        }
    }

    // A clock a test moves by hand, so the order of the list does not hang on how fast the test runs.
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class NoteList
    {
        public List<string> Items { get; set; } = [];
    }

    private sealed class Notes : ToolCollection
    {
        [Tool("AddNote", "Keeps a note.", Kind = ToolKind.State)]
        public string AddNote(string text)
        {
            NoteList list = SessionState(() => new NoteList());
            list.Items.Add(text);
            SetSessionState(list);
            return "noted";
        }

        public IReadOnlyList<string> Of(AgentSession session) => SessionState<NoteList>(session)?.Items ?? [];
    }

    private static ChatMessage Text(string text) => new(ChatRole.Assistant, text);

    private static ChatMessage Call(string name, string argument, string value) =>
        new(ChatRole.Assistant, [new FunctionCallContent($"call-{name}", name, new Dictionary<string, object?> { [argument] = value })]);

    private static AIAgent Agent(ScriptedChatClient model, params AITool[] tools) =>
        model.CreateAgent().WithoutDefaults().WithRole("r").WithTools(tools).Build();

    private static AIFunction Tool(string name) => AIFunctionFactory.Create(() => $"{name} ran", name);

    [Fact]
    public async Task ARestoredSessionsNextRequest_HoldsTheSavedMessagesFollowedByTheNewPrompt()
    {
        CancellationToken none = TestContext.Current.CancellationToken;
        SessionStore store = new(directory);
        AIAgent first = Agent(new ScriptedChatClient(Call("Search", "query", "tea"), Text("It is green.")), AIFunctionFactory.Create((string query) => $"found {query}", "Search"));
        AgentSession session = await first.CreateSessionAsync(none);
        await first.RunAsync("What colour is tea?", session, cancellationToken: none);
        SavedSession saved = await store.SaveAsync(first, session, "What colour is tea?", none);

        ScriptedChatClient later = new(Text("Still green."));
        AIAgent second = Agent(later, AIFunctionFactory.Create((string query) => $"found {query}", "Search"));
        RestoredSession restored = await new SessionStore(directory).RestoreAsync(second, saved.Id, none);
        await second.RunAsync("And now?", restored.Session, cancellationToken: none);

        List<ChatMessage> request = Assert.Single(later.Requests);
        Assert.Equal(["What colour is tea?", "", "", "It is green.", "And now?"], request.Where(message => message.Role != ChatRole.System).Select(message => message.Text));
        Assert.Equal("Search", Assert.Single(request.SelectMany(message => message.Contents).OfType<FunctionCallContent>()).Name);
        Assert.Single(request.SelectMany(message => message.Contents).OfType<FunctionResultContent>());
        Assert.True(restored.SameTools);
        Assert.Equal(saved, restored.Saved with { Tools = saved.Tools });
    }

    [Fact]
    public async Task Save_WritesOneFilePerSession_KeepsItsFirstPrompt_AndARestoredSessionIsSavedToTheFileItCameFrom()
    {
        CancellationToken none = TestContext.Current.CancellationToken;
        Clock clock = new();
        SessionStore store = new(directory, clock);
        AIAgent agent = Agent(new ScriptedChatClient(Text("one"), Text("two"), Text("three")), Tool("Search"));
        AgentSession session = await agent.CreateSessionAsync(none);

        await agent.RunAsync("first prompt", session, cancellationToken: none);
        SavedSession first = await store.SaveAsync(agent, session, "  first prompt\n", none);
        clock.Now = clock.Now.AddMinutes(5);
        await agent.RunAsync("second prompt", session, cancellationToken: none);
        SavedSession second = await store.SaveAsync(agent, session, "second prompt", none);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("first prompt", second.FirstPrompt);
        Assert.Equal(clock.Now, second.SavedAt);
        Assert.Equal(["Search"], second.Tools);
        Assert.Equal(Path.Combine(directory, $"{first.Id}.json"), second.FilePath);
        Assert.Equal([second.FilePath], Directory.GetFiles(directory));

        RestoredSession restored = await store.RestoreAsync(agent, first.Id, none);
        await agent.RunAsync("third prompt", restored.Session, cancellationToken: none);
        SavedSession third = await store.SaveAsync(agent, restored.Session, "third prompt", none);

        Assert.Equal(first.Id, third.Id);
        Assert.Equal("first prompt", third.FirstPrompt);
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task PerSessionCollectionState_ComesBackWithTheSession()
    {
        CancellationToken none = TestContext.Current.CancellationToken;
        SessionStore store = new(directory);
        Notes notes = new();
        AIAgent agent = new ScriptedChatClient(Call("AddNote", "text", "the build is red"), Text("noted")).CreateAgent().WithoutDefaults().WithRole("r").WithTools(notes).Build();
        AgentSession session = await agent.CreateSessionAsync(none);
        await agent.RunAsync("Remember it.", session, cancellationToken: none);
        SavedSession saved = await store.SaveAsync(agent, session, "Remember it.", none);

        Notes later = new();
        AIAgent second = new ScriptedChatClient(Text("x")).CreateAgent().WithoutDefaults().WithRole("r").WithTools(later).Build();
        RestoredSession restored = await store.RestoreAsync(second, saved.Id, none);

        Assert.Equal(["the build is red"], later.Of(restored.Session));
    }

    [Fact]
    public async Task ASessionSavedWithAToolTheAgentNowLacks_IsRestored_AndTheDifferenceNamesTheTools()
    {
        CancellationToken none = TestContext.Current.CancellationToken;
        SessionStore store = new(directory);
        AIAgent first = Agent(new ScriptedChatClient(Call("Deploy", "target", "prod"), Text("deployed")),
            AIFunctionFactory.Create((string target) => $"deployed to {target}", "Deploy"), Tool("Search"));
        AgentSession session = await first.CreateSessionAsync(none);
        await first.RunAsync("Ship it.", session, cancellationToken: none);
        SavedSession saved = await store.SaveAsync(first, session, "Ship it.", none);
        Assert.Equal(["Deploy", "Search"], saved.Tools);

        ScriptedChatClient later = new(Text("It went to prod."));
        AIAgent second = Agent(later, Tool("search"), Tool("Rollback"));
        RestoredSession restored = await store.RestoreAsync(second, saved.Id, none);
        await second.RunAsync("Where did it go?", restored.Session, cancellationToken: none);

        Assert.Equal(["Deploy"], restored.MissingTools);
        Assert.Equal(["Rollback"], restored.NewTools);
        Assert.False(restored.SameTools);
        // The call to the tool that is gone is still in the history the model reads.
        Assert.Contains(later.Requests[0].SelectMany(message => message.Contents).OfType<FunctionCallContent>(), call => call.Name == "Deploy");
    }

    [Fact]
    public async Task List_IsNewestFirst_AndEmptyWithoutAFolder()
    {
        CancellationToken none = TestContext.Current.CancellationToken;
        Clock clock = new();
        SessionStore store = new(directory, clock);
        Assert.Empty(store.List());
        AIAgent agent = Agent(new ScriptedChatClient(Text("a"), Text("b"), Text("c")));
        AgentSession older = await agent.CreateSessionAsync(none);
        AgentSession newer = await agent.CreateSessionAsync(none);

        await agent.RunAsync("older", older, cancellationToken: none);
        await store.SaveAsync(agent, older, "older", none);
        clock.Now = clock.Now.AddHours(1);
        await agent.RunAsync("newer", newer, cancellationToken: none);
        await store.SaveAsync(agent, newer, "newer", none);
        Assert.Equal(["newer", "older"], store.List().Select(session => session.FirstPrompt));

        // Going on with the older conversation makes it the one saved last.
        clock.Now = clock.Now.AddHours(1);
        await agent.RunAsync("more", older, cancellationToken: none);
        await store.SaveAsync(agent, older, "more", none);

        IReadOnlyList<SavedSession> listed = store.List();
        Assert.Equal(["older", "newer"], listed.Select(session => session.FirstPrompt));
        Assert.All(listed, session => Assert.True(session.IsReadable));
        Assert.Equal(clock.Now, listed[0].SavedAt);
    }

    [Theory]
    [InlineData("{ not json", "not valid JSON")]
    [InlineData("{\"id\":\"x\",\"firstPrompt\":\"hi\"}", "not a session this store wrote")]
    [InlineData("[1, 2, 3]", "not a session this store wrote")]
    public async Task AFileThatCannotBeRead_IsListedAsUnreadable_IsNotRestored_AndStaysOnDisk(string content, string problem)
    {
        CancellationToken none = TestContext.Current.CancellationToken;
        SessionStore store = new(directory);
        AIAgent agent = Agent(new ScriptedChatClient(Text("a")));
        AgentSession session = await agent.CreateSessionAsync(none);
        await agent.RunAsync("good", session, cancellationToken: none);
        await store.SaveAsync(agent, session, "good", none);
        string broken = Path.Combine(directory, "20261008-090000-broken.json");
        File.WriteAllText(broken, content);

        IReadOnlyList<SavedSession> listed = store.List();

        Assert.Equal(2, listed.Count);
        SavedSession unreadable = Assert.Single(listed, saved => !saved.IsReadable);
        Assert.Equal("20261008-090000-broken", unreadable.Id);
        Assert.Contains(problem, unreadable.Problem);
        Assert.Equal("good", Assert.Single(listed, saved => saved.IsReadable).FirstPrompt);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => store.RestoreAsync(agent, unreadable.Id, none));
        Assert.Contains("cannot be read", error.Message);
        Assert.Contains("left where it is", error.Message);
        Assert.Equal(content, File.ReadAllText(broken));
    }

    [Fact]
    public async Task ASessionTheAgentCannotRead_ThrowsNamingTheFile_AndLeavesIt()
    {
        CancellationToken none = TestContext.Current.CancellationToken;
        SessionStore store = new(directory);
        AIAgent agent = Agent(new ScriptedChatClient(Text("a")));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "odd.json");
        File.WriteAllText(path, "{\"id\":\"odd\",\"firstPrompt\":\"hi\",\"savedAt\":\"2026-10-08T09:00:00+00:00\",\"tools\":[],\"session\":\"not a session\"}");

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => store.RestoreAsync(agent, "odd", none));

        Assert.Contains("cannot read the saved session 'odd'", error.Message);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task RestoringAnIdThatIsNotThere_ThrowsListingTheSessions_AndBlankValuesThrow()
    {
        CancellationToken none = TestContext.Current.CancellationToken;
        SessionStore store = new(directory);
        AIAgent agent = Agent(new ScriptedChatClient(Text("a")));
        AgentSession session = await agent.CreateSessionAsync(none);

        ArgumentException empty = await Assert.ThrowsAsync<ArgumentException>(() => store.RestoreAsync(agent, "nope", none));
        Assert.Contains("holds no session", empty.Message);

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(agent, session, " ", none));
        await agent.RunAsync("hello", session, cancellationToken: none);
        SavedSession saved = await store.SaveAsync(agent, session, "hello", none);

        ArgumentException missing = await Assert.ThrowsAsync<ArgumentException>(() => store.RestoreAsync(agent, "nope", none));
        Assert.Contains($"'{saved.Id}'", missing.Message);
        await Assert.ThrowsAsync<ArgumentException>(() => store.RestoreAsync(agent, "../outside", none));
        Assert.Throws<ArgumentException>(() => new SessionStore(" "));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }
}
