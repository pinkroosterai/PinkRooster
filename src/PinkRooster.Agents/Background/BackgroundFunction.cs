using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace PinkRooster.Agents.Background;

/// <summary>
/// A tool the host allowed in the background: the same tool with one more optional parameter, <c>runInBackground</c>. Without it
/// the call goes to the tool unchanged; with it the call starts a task and returns at once.
/// </summary>
internal sealed class BackgroundFunction : DelegatingAIFunction
{
    public const string ParameterName = "runInBackground";

    private const string ParameterDescription =
        "Optional. True starts this call as a background task and returns a task id at once instead of the result; " +
        "WaitForTasks returns the result once the task has ended. Leave it out when you need the result before your next step.";

    private readonly JsonElement schema;

    public BackgroundFunction(AIFunction innerFunction) : base(innerFunction)
    {
        schema = WithParameter(innerFunction.JsonSchema);
    }

    public override JsonElement JsonSchema => schema;

    /// <summary>Whether <paramref name="function"/> already has a parameter of the name this wrapper adds, ignoring case.</summary>
    public static bool HasParameter(AIFunction function) =>
        function.JsonSchema.ValueKind == JsonValueKind.Object
        && function.JsonSchema.TryGetProperty("properties", out JsonElement properties)
        && properties.ValueKind == JsonValueKind.Object
        && properties.EnumerateObject().Any(property => string.Equals(property.Name, ParameterName, StringComparison.OrdinalIgnoreCase));

    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        // A copy without the added argument: the tool never sees it, and the caller's arguments stay as the model sent them.
        AIFunctionArguments forTool = new(arguments) { Services = arguments.Services, Context = arguments.Context };
        bool inBackground = forTool.Remove(ParameterName, out object? value) && IsTrue(value);
        if (!inBackground)
        {
            return InnerFunction.InvokeAsync(forTool, cancellationToken);
        }

        if (BackgroundTaskStore.Current is not BackgroundTaskStore store)
        {
            return ValueTask.FromResult<object?>(
                $"Error: {Name} cannot run in the background here, because no run with background tasks is in progress. Call it again without {ParameterName}.");
        }
        return ValueTask.FromResult<object?>(store.Start(InnerFunction, forTool));
    }

    private static bool IsTrue(object? value) => value switch
    {
        bool flag => flag,
        JsonElement { ValueKind: JsonValueKind.True } => true,
        JsonElement { ValueKind: JsonValueKind.String } element => bool.TryParse(element.GetString(), out bool parsed) && parsed,
        string text => bool.TryParse(text, out bool parsed) && parsed,
        _ => false
    };

    private static JsonElement WithParameter(JsonElement original)
    {
        JsonObject root = original.ValueKind == JsonValueKind.Object ? JsonObject.Create(original)! : new JsonObject { ["type"] = "object" };
        if (root["properties"] is not JsonObject properties)
        {
            properties = [];
            root["properties"] = properties;
        }
        properties[ParameterName] = new JsonObject { ["type"] = "boolean", ["description"] = ParameterDescription };
        return JsonSerializer.SerializeToElement(root, AIJsonUtilities.DefaultOptions);
    }
}
