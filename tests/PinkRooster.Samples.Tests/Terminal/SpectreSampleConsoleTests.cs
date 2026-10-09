using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using PinkRooster.Agents.Eventing;
using PinkRooster.Samples.Models;
using PinkRooster.Samples.Terminal;
using Spectre.Console.Testing;

namespace PinkRooster.Samples.Tests.Terminal;

public sealed class SpectreSampleConsoleTests
{    [Fact]
    public void WriteEvent_ForwardsToTheAgentConsole()
    {
        TestConsole console = new();

        new SpectreSampleConsole(console).WriteEvent(new AssistantTextDelta("It builds."));

        Assert.Contains("  It builds.", Normalize(console.Output));
    }

    [Fact]
    public void WriteError_GivesHttpGuidanceAndRedactsCredentials()
    {
        TestConsole console = new();

        new SpectreSampleConsole(console, ["secret-key"]).WriteError(new ClientResultException("Unauthorized for secret-key", new StatusResponse(401)));

        string output = Normalize(console.Output);
        Assert.Contains("Request failed with HTTP 401. Check this model's apiKey in models.json", output);
        Assert.DoesNotContain("secret-key", output);
        Assert.Contains("[redacted]", output);
    }

    [Fact]
    public void WriteModel_NamesTheModelAndHost()
    {
        TestConsole console = new();

        new SpectreSampleConsole(console).WriteModel(new ModelSettings(new Uri("https://api.groq.com/openai/v1"), "openai/gpt-oss-120b", "k"));

        Assert.Contains("PinkRooster sample with openai/gpt-oss-120b on api.groq.com", Normalize(console.Output));
    }

    [Fact]
    public async Task SelectModelAsync_ReturnsTheOnlyModelWithoutAsking()
    {
        TestConsole console = new TestConsole().Interactive();
        ModelSettings only = new(new Uri("http://localhost:11434/v1"), "qwen3.5:9b", null);

        ModelSettings picked = await new SpectreSampleConsole(console).SelectModelAsync([only], TestContext.Current.CancellationToken);

        Assert.Same(only, picked);
        Assert.DoesNotContain("Which model?", console.Output);
    }

    [Fact]
    public async Task SelectModelAsync_WithSeveralModelsReturnsThePickedOne()
    {
        TestConsole console = new TestConsole().Interactive();
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Enter);
        ModelSettings groq = new(new Uri("https://api.groq.com/openai/v1"), "openai/gpt-oss-120b", "k");

        ModelSettings picked = await new SpectreSampleConsole(console).SelectModelAsync(
            [new ModelSettings(new Uri("http://localhost:11434/v1"), "qwen3.5:9b", null), groq], TestContext.Current.CancellationToken);

        Assert.Same(groq, picked);
        Assert.Contains("openai/gpt-oss-120b on api.groq.com", console.Output);
    }

    private static string Normalize(string text) => text.ReplaceLineEndings("\n");

    private sealed class StatusResponse(int status) : PipelineResponse
    {
        public override int Status => status;
        public override string ReasonPhrase => "";
        public override Stream? ContentStream { get; set; }
        public override BinaryData Content => BinaryData.Empty;
        protected override PipelineResponseHeaders HeadersCore => throw new NotSupportedException();
        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => BinaryData.Empty;
        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) => new(BinaryData.Empty);
        public override void Dispose() { }
    }
}
