using Microsoft.Extensions.AI;
using PinkRooster.Samples.Models;

namespace PinkRooster.Samples.Tests.Models;

public sealed class ModelsFileTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("chat-models-").FullName;

    private string ModelsPath => Path.Combine(folder, "models.json");

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public void Load_WhenTheFileIsMissing_WritesTheOllamaDefaultAndReturnsIt()
    {
        IReadOnlyList<ModelSettings> models = ModelsFile.Load(ModelsPath, out bool created);

        Assert.True(created);
        Assert.True(File.Exists(ModelsPath));
        ModelSettings model = Assert.Single(models);
        Assert.Equal(new Uri("http://localhost:11434/v1"), model.Endpoint);
        Assert.Equal("qwen3.5:9b", model.Model);
        Assert.Null(model.ApiKey);

        ModelsFile.Load(ModelsPath, out bool createdAgain);
        Assert.False(createdAgain);
    }

    [Fact]
    public void Load_ReadsEveryModelInOrder()
    {
        File.WriteAllText(ModelsPath, """
            {
              // Comments and trailing commas are fine.
              "models": [
                { "model": "qwen3.5:9b", "endpoint": "http://localhost:11434/v1" },
                { "model": "openai/gpt-oss-120b", "endpoint": "https://api.groq.com/openai/v1", "apiKey": "k" },
              ]
            }
            """);

        IReadOnlyList<ModelSettings> models = ModelsFile.Load(ModelsPath, out bool created);

        Assert.False(created);
        Assert.Equal(
            [new ModelSettings(new Uri("http://localhost:11434/v1"), "qwen3.5:9b", null), new ModelSettings(new Uri("https://api.groq.com/openai/v1"), "openai/gpt-oss-120b", "k")],
            models);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\" \"")]
    public void Load_TreatsABlankKeyAsNone(string apiKey)
    {
        File.WriteAllText(ModelsPath, $$"""{ "models": [ { "model": "m", "endpoint": "http://localhost:1234/v1", "apiKey": {{apiKey}} } ] }""");

        Assert.Null(Assert.Single(ModelsFile.Load(ModelsPath, out _)).ApiKey);
    }

    [Fact]
    public void Load_DefaultsReasoningEffortToMediumWhenNotSet()
    {
        File.WriteAllText(ModelsPath, """{ "models": [ { "model": "m", "endpoint": "http://localhost:1234/v1" } ] }""");

        Assert.Equal(ReasoningEffort.Medium, Assert.Single(ModelsFile.Load(ModelsPath, out _)).ReasoningEffort);
    }

    [Theory]
    [InlineData("none", "None")]
    [InlineData("Low", "Low")]
    [InlineData("MEDIUM", "Medium")]
    [InlineData("high", "High")]
    [InlineData("extraHigh", "ExtraHigh")]
    public void Load_ParsesReasoningEffortCaseInsensitively(string configured, string expected)
    {
        File.WriteAllText(ModelsPath, $$"""{ "models": [ { "model": "m", "endpoint": "http://localhost:1234/v1", "reasoningEffort": "{{configured}}" } ] }""");

        ReasoningEffort effort = Assert.Single(ModelsFile.Load(ModelsPath, out _)).ReasoningEffort;

        Assert.Equal(expected, effort.ToString());
    }

    [Fact]
    public void Load_RejectsAnUnknownReasoningEffort()
    {
        File.WriteAllText(ModelsPath, """{ "models": [ { "model": "m", "endpoint": "http://localhost:1234/v1", "reasoningEffort": "extreme" } ] }""");

        ArgumentException error = Assert.Throws<ArgumentException>(() => ModelsFile.Load(ModelsPath, out _));

        Assert.Contains("\"reasoningEffort\"", error.Message);
        Assert.Contains("extreme", error.Message);
    }

    [Fact]
    public void Load_DefaultsApiToChatCompletionsWhenNotSet()
    {
        File.WriteAllText(ModelsPath, """{ "models": [ { "model": "m", "endpoint": "http://localhost:1234/v1" } ] }""");

        Assert.Equal(ModelApi.ChatCompletions, Assert.Single(ModelsFile.Load(ModelsPath, out _)).Api);
    }

    [Theory]
    [InlineData("chatCompletions", "ChatCompletions")]
    [InlineData("RESPONSES", "Responses")]
    public void Load_ParsesApiCaseInsensitively(string configured, string expected)
    {
        File.WriteAllText(ModelsPath, $$"""{ "models": [ { "model": "m", "endpoint": "http://localhost:1234/v1", "api": "{{configured}}" } ] }""");

        ModelApi api = Assert.Single(ModelsFile.Load(ModelsPath, out _)).Api;

        Assert.Equal(expected, api.ToString());
    }

    [Fact]
    public void Load_RejectsAnUnknownApi()
    {
        File.WriteAllText(ModelsPath, """{ "models": [ { "model": "m", "endpoint": "http://localhost:1234/v1", "api": "assistants" } ] }""");

        ArgumentException error = Assert.Throws<ArgumentException>(() => ModelsFile.Load(ModelsPath, out _));

        Assert.Contains("\"api\"", error.Message);
        Assert.Contains("assistants", error.Message);
    }

    [Theory]
    [InlineData("""{ "models": [ { "model": "m", "endpoint": "ftp://example.com" } ] }""", "Model 1", "endpoint")]
    [InlineData("""{ "models": [ { "model": "m", "endpoint": "http://a/v1" }, { "model": "m", "endpoint": "not a url" } ] }""", "Model 2", "endpoint")]
    [InlineData("""{ "models": [ { "model": " ", "endpoint": "http://localhost:1234/v1" } ] }""", "Model 1", "\"model\"")]
    [InlineData("""{ "models": [] }""", "lists no models", "delete the file")]
    [InlineData("""{ }""", "lists no models", "delete the file")]
    [InlineData("""{ "models": [ """, "is not valid JSON", "delete it")]
    public void Load_RejectsAFileToFix_NamingTheFileAndTheFix(string content, string problem, string fix)
    {
        File.WriteAllText(ModelsPath, content);

        ArgumentException error = Assert.Throws<ArgumentException>(() => ModelsFile.Load(ModelsPath, out _));

        Assert.Contains(ModelsPath, error.Message);
        Assert.Contains(problem, error.Message);
        Assert.Contains(fix, error.Message);
    }

    [Fact]
    public void Load_ReadsTheTrackedExampleFile()
    {
        IReadOnlyList<ModelSettings> models = ModelsFile.Load(Path.Combine(AppContext.BaseDirectory, "models.example.json"), out bool created);

        Assert.False(created);
        Assert.Equal(4, models.Count);
    }
}
