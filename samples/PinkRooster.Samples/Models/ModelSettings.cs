using Microsoft.Extensions.AI;

namespace PinkRooster.Samples.Models;

/// <summary>Which OpenAI-compatible surface a model is called through.</summary>
public enum ModelApi
{
    /// <summary><c>/v1/chat/completions</c>. Every configured endpoint supports this.</summary>
    ChatCompletions,

    /// <summary><c>/v1/responses</c>. OpenAI-specific; some of its models require it for function tools with reasoning.</summary>
    Responses
}

/// <summary>Where a sample's model lives: an OpenAI-compatible endpoint, one entry of <c>models.json</c>.</summary>
/// <param name="ApiKey">The endpoint's credential; null for a local server such as Ollama, which needs none.</param>
/// <param name="ReasoningEffort">
/// How hard the model should reason, sent as <c>reasoning_effort</c>. Some models reject that field
/// alongside function tools on <see cref="ModelApi.ChatCompletions"/>; <see cref="ReasoningEffort.None"/> omits it.
/// </param>
/// <param name="Api">Which surface to call the model through.</param>
public sealed record ModelSettings(Uri Endpoint, string Model, string? ApiKey, ReasoningEffort ReasoningEffort = ReasoningEffort.Medium, ModelApi Api = ModelApi.ChatCompletions)
{
    public const string DefaultEndpoint = "http://localhost:11434/v1";
    public const string DefaultModel = "qwen3.5:9b";
}
