namespace PinkRooster.ToolCollections;

/// <summary>
/// Adds instructions to the system prompt of every agent given the <see cref="ToolCollection"/>.
/// </summary>
/// <remarks>
/// Use it for what spans the collection, such as its environment or workflow; when to use one tool belongs in that tool's description.
/// The order between several of these attributes on one class isn't guaranteed, so put text whose order matters in one attribute.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
public sealed class ToolCollectionInstructionAttribute : Attribute
{
    /// <summary>The instructions, in order.</summary>
    public string[] Instructions { get; }

    /// <param name="instructions">One or more instructions; each must be non-blank.</param>
    public ToolCollectionInstructionAttribute(params string[] instructions)
    {
        Instructions = instructions ?? [];
    }
}
