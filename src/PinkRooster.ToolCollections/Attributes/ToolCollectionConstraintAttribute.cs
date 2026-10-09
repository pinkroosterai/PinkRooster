namespace PinkRooster.ToolCollections;

/// <summary>
/// Adds constraints to the system prompt of every agent given the <see cref="ToolCollection"/>.
/// </summary>
/// <remarks>
/// Use it for boundaries that span the collection, such as a host policy; how to call one tool belongs in that tool's description.
/// The order between several of these attributes on one class isn't guaranteed, so put text whose order matters in one attribute.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
public sealed class ToolCollectionConstraintAttribute : Attribute
{
    /// <summary>The constraints, in order.</summary>
    public string[] Constraints { get; }

    /// <param name="constraints">One or more constraints; each must be non-blank.</param>
    public ToolCollectionConstraintAttribute(params string[] constraints)
    {
        Constraints = constraints ?? [];
    }
}
