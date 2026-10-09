using Microsoft.Extensions.AI;
using PinkRooster.Agents.Tests.TestSupport;
using PinkRooster.Samples.Models;
using PinkRooster.Samples.Terminal;
using Spectre.Console.Testing;

namespace PinkRooster.Samples.Tests;

public sealed class SampleHostTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("sample-host-").FullName;

    private string ModelsPath => Path.Combine(folder, "models.json");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public async Task RunAsync_WithoutAModelsFile_WritesTheDefault_RunsTheSampleAndReturnsZero()
    {
        TestConsole console = new();
        ModelSettings? used = null;

        int code = await SampleHost.RunAsync(ModelsPath, console, settings =>
        {
            used = settings;
            return new ScriptedChatClient(new ChatMessage(ChatRole.Assistant, "ok"));
        }, (_, _, _) => Task.CompletedTask, needsTerminal: false, Token);

        Assert.Equal(0, code);
        Assert.True(File.Exists(ModelsPath));
        Assert.Equal(ModelSettings.DefaultModel, used?.Model);
        Assert.Contains(ModelsPath, console.Output);
    }

    [Fact]
    public async Task RunAsync_WithABrokenModelsFile_NamesTheFileAndReturnsOne()
    {
        File.WriteAllText(ModelsPath, "{ \"models\": [] }");
        TestConsole console = new();
        bool ran = false;

        int code = await SampleHost.RunAsync(ModelsPath, console, _ => new ScriptedChatClient(new ChatMessage(ChatRole.Assistant, "ok")), (_, _, _) =>
        {
            ran = true;
            return Task.CompletedTask;
        }, needsTerminal: false, Token);

        Assert.Equal(1, code);
        Assert.False(ran);
        Assert.Contains(ModelsPath, console.Output.Replace("\r", "").Replace("\n", ""));
    }

    [Fact]
    public async Task RunAsync_WhenTheModelServerIsUnreachable_SaysWhereToLookAndReturnsOne()
    {
        TestConsole console = new();

        int code = await SampleHost.RunAsync(ModelsPath, console, _ => new ScriptedChatClient(new ChatMessage(ChatRole.Assistant, "ok")), (_, _, _) => throw new HttpRequestException("refused"), needsTerminal: false, Token);

        Assert.Equal(1, code);
        Assert.Contains("Could not reach the model server", console.Output);
        Assert.Contains("models.json", console.Output);
    }

    [Fact]
    public async Task RunAsync_ForASampleThatAsksQuestions_WithoutATerminal_ReturnsOneBeforeCallingTheModel()
    {
        TestConsole console = new();
        bool built = false;

        int code = await SampleHost.RunAsync(ModelsPath, console, _ =>
        {
            built = true;
            return new ScriptedChatClient(new ChatMessage(ChatRole.Assistant, "ok"));
        }, (_, _, _) => Task.CompletedTask, needsTerminal: true, Token);

        Assert.Equal(1, code);
        Assert.False(built);
        Assert.Contains("interactive terminal", console.Output);
    }

    [Fact]
    public async Task RunAsync_WhenCancelled_Returns130()
    {
        using CancellationTokenSource cancel = new();
        await cancel.CancelAsync();

        int code = await SampleHost.RunAsync(ModelsPath, new TestConsole(), _ => new ScriptedChatClient(new ChatMessage(ChatRole.Assistant, "ok")),
            (_, _, token) => Task.Delay(Timeout.Infinite, token), needsTerminal: false, cancel.Token);

        Assert.Equal(SampleHost.Cancelled, code);
    }

    [Fact]
    public async Task RunAsync_HandsTheSampleTheClientTheConsoleAndTheToken()
    {
        ScriptedChatClient client = new(new ChatMessage(ChatRole.Assistant, "ok"));
        ISampleConsole? seenConsole = null;
        IChatClient? seenClient = null;

        await SampleHost.RunAsync(ModelsPath, new TestConsole(), _ => client, (chat, screen, _) =>
        {
            seenClient = chat;
            seenConsole = screen;
            return Task.CompletedTask;
        }, needsTerminal: false, Token);

        Assert.Same(client, seenClient!.GetService<ScriptedChatClient>());
        Assert.IsType<SpectreSampleConsole>(seenConsole);
    }

    [Fact]
    public async Task RunAsync_ForASampleThatUsesTools_SaysWhenTheModelCalledNone()
    {
        TestConsole console = new();

        int code = await SampleHost.RunAsync(ModelsPath, console, _ => new ScriptedChatClient(new ChatMessage(ChatRole.Assistant, "plain text")),
            (client, _, token) => client.GetResponseAsync("go", cancellationToken: token), needsTerminal: false, Token, usesTools: true);

        Assert.Equal(0, code);
        Assert.Contains("The model called no tool", console.Output);
    }

    [Fact]
    public async Task RunAsync_ForASampleThatUsesTools_SaysNothingWhenTheModelCalledOne()
    {
        TestConsole console = new();
        ScriptedChatClient model = new(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c1", "GetTicket")]));

        await SampleHost.RunAsync(ModelsPath, console, _ => model,
            (client, _, token) => client.GetResponseAsync("go", cancellationToken: token), needsTerminal: false, Token, usesTools: true);

        Assert.DoesNotContain("The model called no tool", console.Output);
    }

    [Fact]
    public async Task RunAsync_CountsToolCallsInAStreamedRun()
    {
        TestConsole console = new();
        ScriptedChatClient model = new(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c1", "GetTicket")]));

        await SampleHost.RunAsync(ModelsPath, console, _ => model, async (client, _, token) =>
        {
            await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync("go", cancellationToken: token))
            {
            }
        }, needsTerminal: false, Token, usesTools: true);

        Assert.DoesNotContain("The model called no tool", console.Output);
    }
}
