using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace PinkRooster.Samples.Models;

/// <summary>Reads the models a sample can use from <c>models.json</c>, writing an Ollama default the first time.</summary>
public static class ModelsFile
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>The shared library's own <c>models.json</c>, set at build time, so every working directory uses the same file.</summary>
    public static string DefaultPath =>
        typeof(ModelsFile).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(attribute => attribute.Key == "ModelsFile").Value
        ?? throw new InvalidOperationException("The ModelsFile assembly metadata has no value; check PinkRooster.Samples.csproj.");

    /// <param name="created">True when the file did not exist and the Ollama default was written.</param>
    /// <exception cref="ArgumentException">The file is not valid JSON, lists no models, or has an entry to fix; the message names it.</exception>
    public static IReadOnlyList<ModelSettings> Load(string path, out bool created)
    {
        created = !File.Exists(path);
        if (created)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, DefaultContent);
        }

        ModelsDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<ModelsDocument>(File.ReadAllText(path), Options);
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

    private static ModelSettings Validate(string path, int index, ModelEntry? entry)
    {
        string where = $"Model {index + 1} in {path}";
        if (string.IsNullOrWhiteSpace(entry?.Model))
        {
            throw new ArgumentException($"{where} has no \"model\". Set it to the model ID, such as {ModelSettings.DefaultModel}.");
        }

        if (!Uri.TryCreate(entry.Endpoint, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"{where} needs an \"endpoint\" that is an absolute HTTP or HTTPS URL, such as {ModelSettings.DefaultEndpoint}.");
        }

        ReasoningEffort effort = ParseReasoningEffort(where, entry.ReasoningEffort);
        ModelApi api = ParseApi(where, entry.Api);

        return new ModelSettings(uri, entry.Model.Trim(), string.IsNullOrWhiteSpace(entry.ApiKey) ? null : entry.ApiKey, effort, api);
    }

    private static ReasoningEffort ParseReasoningEffort(string where, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ReasoningEffort.Medium;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "none" => ReasoningEffort.None,
            "low" => ReasoningEffort.Low,
            "medium" => ReasoningEffort.Medium,
            "high" => ReasoningEffort.High,
            "extrahigh" => ReasoningEffort.ExtraHigh,
            _ => throw new ArgumentException($"{where} has a \"reasoningEffort\" of \"{value}\"; use \"none\", \"low\", \"medium\", \"high\" or \"extraHigh\".")
        };
    }

    private static ModelApi ParseApi(string where, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ModelApi.ChatCompletions;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "chatcompletions" => ModelApi.ChatCompletions,
            "responses" => ModelApi.Responses,
            _ => throw new ArgumentException($"{where} has an \"api\" of \"{value}\"; use \"chatCompletions\" or \"responses\".")
        };
    }

    private static string DefaultContent => $$"""
        {
          "models": [
            { "model": "{{ModelSettings.DefaultModel}}", "endpoint": "{{ModelSettings.DefaultEndpoint}}", "apiKey": null }
          ]
        }

        """;

    private sealed record ModelsDocument(List<ModelEntry?>? Models);

    private sealed record ModelEntry(string? Model, string? Endpoint, string? ApiKey, string? ReasoningEffort, string? Api);
}
