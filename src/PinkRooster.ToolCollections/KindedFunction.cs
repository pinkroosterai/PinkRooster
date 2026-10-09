using Microsoft.Extensions.AI;

namespace PinkRooster.ToolCollections;

/// <summary>
/// A function from elsewhere with a <see cref="ToolKind"/> added to its properties: such a function's own properties cannot be
/// written, so the kind is laid over them.
/// </summary>
internal sealed class KindedFunction : DelegatingAIFunction
{
    private readonly IReadOnlyDictionary<string, object?> properties;

    public KindedFunction(AIFunction innerFunction, ToolKind kind) : base(innerFunction)
    {
        properties = new Dictionary<string, object?>(innerFunction.AdditionalProperties) { [ToolKindExtensions.Key] = kind };
    }

    public override IReadOnlyDictionary<string, object?> AdditionalProperties => properties;
}
