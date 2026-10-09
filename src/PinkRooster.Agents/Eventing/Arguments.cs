namespace PinkRooster.Agents.Eventing;

internal static class Arguments
{
    /// <summary>A read-only copy of a tool call's arguments, so a handler can neither see later changes nor make them.</summary>
    public static IReadOnlyDictionary<string, object?> Of(IEnumerable<KeyValuePair<string, object?>>? arguments) =>
        arguments is null ? new Dictionary<string, object?>() : new Dictionary<string, object?>(arguments);
}
