using System.Text.Json;
using Microsoft.Extensions.AI;

namespace PinkRooster.Samples.CodingAgent.ModelSettings;

/// <summary>Reads the models the assistant can use from its settings file, writing an Ollama default the first time.</summary>
public static class ModelSettingsFile
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>The file under the user's application-data folder, outside any workspace, so keys never reach a repository.</summary>
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "PinkRooster", "CodingAgent", "models.json");

    /// <param name="path">The settings file.</param>
    /// <param name="created">True when the file did not exist and the Ollama default was written.</param>
    /// <exception cref="ArgumentException">The file is not valid JSON, lists no models, or has an entry to fix; the message names it.</exception>
    public static IReadOnlyList<ModelEntry> Load(string path, out bool created)
    {
        created = !File.Exists(path);
        if (created)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, DefaultContent);
        }

        Document? document;
        try
        {
            document = JsonSerializer.Deserialize<Document>(File.ReadAllText(path), Options);
        }
        catch (JsonException error)
        {
            throw new ArgumentException($"{path} is not valid JSON: {error.Message} Fix it, or delete it to start again from the Ollama default.", error);
        }

        if (document?.Models is not { Count: > 0 } entries)
        {
            throw new ArgumentException($"{path} lists no models. Add one to \"models\", or delete the file to start again from the Ollama default.");
        }

        return [.. entries.Select((entry, index) => Validate(path, index, entry))];
    }

    private static ModelEntry Validate(string path, int index, Entry? entry)
    {
        string where = $"Model {index + 1} in {path}";
        if (string.IsNullOrWhiteSpace(entry?.Model))
        {
            throw new ArgumentException($"{where} has no \"model\". Set it to the model ID, such as {ModelEntry.DefaultModel}.");
        }

        if (!Uri.TryCreate(entry.Endpoint, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"{where} needs an \"endpoint\" that is an absolute HTTP or HTTPS URL, such as {ModelEntry.DefaultEndpoint}.");
        }

        if (entry.ContextSize is <= 0)
        {
            throw new ArgumentException($"{where} has a \"contextSize\" of {entry.ContextSize}; give the model's context window in tokens, or leave it out.");
        }

        if (entry.CompactAt is <= 0 or > 1)
        {
            throw new ArgumentException($"{where} has a \"compactAt\" of {entry.CompactAt}; give a share of the context window above 0 and up to 1, or leave it out for {ModelEntry.DefaultCompactAt}.");
        }

        ReasoningEffort effort = entry.ReasoningEffort?.Trim().ToLowerInvariant() switch
        {
            null or "" or "medium" => ReasoningEffort.Medium,
            "none" => ReasoningEffort.None,
            "low" => ReasoningEffort.Low,
            "high" => ReasoningEffort.High,
            "extrahigh" => ReasoningEffort.ExtraHigh,
            _ => throw new ArgumentException($"{where} has a \"reasoningEffort\" of \"{entry.ReasoningEffort}\"; use \"none\", \"low\", \"medium\", \"high\" or \"extraHigh\".")
        };
        ModelApi api = entry.Api?.Trim().ToLowerInvariant() switch
        {
            null or "" or "chatcompletions" => ModelApi.ChatCompletions,
            "responses" => ModelApi.Responses,
            _ => throw new ArgumentException($"{where} has an \"api\" of \"{entry.Api}\"; use \"chatCompletions\" or \"responses\".")
        };

        return new ModelEntry(
            uri,
            entry.Model.Trim(),
            string.IsNullOrWhiteSpace(entry.ApiKey) ? null : entry.ApiKey,
            effort,
            api,
            string.IsNullOrWhiteSpace(entry.UseWhen) ? ModelEntry.DefaultUseWhen : entry.UseWhen.Trim(),
            entry.ContextSize,
            entry.CompactAt ?? ModelEntry.DefaultCompactAt,
            entry.Vision ?? false);
    }

    private static string DefaultContent => $$"""
        {
          // The models the coding assistant can use. The first is asked for at start when there are several;
          // each is also offered to the assistant as a model for its helpers, with "useWhen" as the hint.
          // Optional per model: "apiKey", "reasoningEffort" (none, low, medium, high, extraHigh),
          // "api" (chatCompletions, responses), "useWhen", "contextSize" (tokens; a long conversation is
          // shortened when a request passes "compactAt" of it, 0.75 unless set), "vision" (true for a model that
          // accepts images; with one, the assistant can look at screenshots and other image files).
          "models": [
            { "model": "{{ModelEntry.DefaultModel}}", "endpoint": "{{ModelEntry.DefaultEndpoint}}", "apiKey": null }
          ]
        }

        """;

    private sealed record Document(List<Entry?>? Models);

    private sealed record Entry(string? Model, string? Endpoint, string? ApiKey, string? ReasoningEffort, string? Api, string? UseWhen, int? ContextSize, double? CompactAt, bool? Vision);
}
