using Microsoft.Extensions.AI;

namespace PinkRooster.Samples.CodingAgent.ModelSettings;

/// <summary>Which OpenAI-compatible surface a model is called through.</summary>
public enum ModelApi
{
    /// <summary><c>/v1/chat/completions</c>. Every configured endpoint supports this.</summary>
    ChatCompletions,

    /// <summary><c>/v1/responses</c>. OpenAI-specific; some of its models require it for function tools with reasoning.</summary>
    Responses
}

/// <summary>One model the assistant can use: an OpenAI-compatible endpoint, one entry of the model settings file.</summary>
/// <param name="Endpoint">The endpoint's base URL.</param>
/// <param name="Model">The model ID.</param>
/// <param name="ApiKey">The endpoint's credential; null for a local server such as Ollama, which needs none.</param>
/// <param name="ReasoningEffort">How hard the model should reason, sent as <c>reasoning_effort</c>; <see cref="ReasoningEffort.None"/> omits it.</param>
/// <param name="Api">Which surface to call the model through.</param>
/// <param name="UseWhen">When a helper should run on this model, in a line the assistant reads.</param>
/// <param name="ContextSize">The model's context window in tokens, or null when it is not known; without it a conversation is only compacted on request.</param>
/// <param name="CompactAt">The share of the context window at which a conversation is compacted.</param>
/// <param name="Vision">True when the model accepts images; the assistant's image tools send their images to such a model.</param>
public sealed record ModelEntry(
    Uri Endpoint,
    string Model,
    string? ApiKey,
    ReasoningEffort ReasoningEffort = ReasoningEffort.Medium,
    ModelApi Api = ModelApi.ChatCompletions,
    string UseWhen = ModelEntry.DefaultUseWhen,
    int? ContextSize = null,
    double CompactAt = ModelEntry.DefaultCompactAt,
    bool Vision = false)
{
    public const double DefaultCompactAt = 0.75;
    public const string DefaultEndpoint = "http://localhost:11434/v1";
    public const string DefaultModel = "qwen3.5:9b";
    public const string DefaultUseWhen = "General work.";
}
