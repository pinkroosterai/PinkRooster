using System.Text.Json.Serialization;

namespace PinkRooster.ToolCollections.BuiltIn.TaskLists;

/// <summary>Source-generated JSON metadata for the stored task list, so the JSON storage needs no reflection.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TaskListSnapshot))]
internal sealed partial class TaskListJsonContext : JsonSerializerContext;
