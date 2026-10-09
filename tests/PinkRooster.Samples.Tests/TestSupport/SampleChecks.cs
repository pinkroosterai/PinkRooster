using Microsoft.Extensions.AI;
using PinkRooster.Samples.Models;
using PinkRooster.Samples.Terminal;
using Spectre.Console.Testing;

namespace PinkRooster.Samples.Tests.TestSupport;

/// <summary>Checks every sample shares: what its host does when the model is not there.</summary>
internal static class SampleChecks
{
    /// <summary>A model server that cannot be reached, as a sample sees it with a fresh <c>models.json</c> and no Ollama running.</summary>
    private sealed class UnreachableChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("refused");

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("refused");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    /// <summary>With a fresh <c>models.json</c> and no reachable model, the sample exits 1 and says to check the endpoint in <c>models.json</c>.</summary>
    public static async Task AssertExitsNamingModelsFile(Func<IChatClient, ISampleConsole, CancellationToken, Task> run, bool needsTerminal = false)
    {
        string folder = Directory.CreateTempSubdirectory("sample-checks-").FullName;
        try
        {
            TestConsole console = new TestConsole().Interactive();
            console.Input.PushTextWithEnter("hello");

            int code = await SampleHost.RunAsync(Path.Combine(folder, "models.json"), console, _ => new UnreachableChatClient(), run, needsTerminal, TestContext.Current.CancellationToken);

            Assert.Equal(1, code);
            Assert.Contains("Could not reach the model server", console.Output);
            Assert.Contains("models.json", console.Output);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>A sample that asks the user something exits 1, saying so, when there is no interactive terminal.</summary>
    public static async Task AssertNeedsATerminal(Func<IChatClient, ISampleConsole, CancellationToken, Task> run)
    {
        string folder = Directory.CreateTempSubdirectory("sample-checks-").FullName;
        try
        {
            TestConsole console = new();

            int code = await SampleHost.RunAsync(Path.Combine(folder, "models.json"), console, _ => new UnreachableChatClient(), run, needsTerminal: true, TestContext.Current.CancellationToken);

            Assert.Equal(1, code);
            Assert.Contains("interactive terminal", console.Output);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
