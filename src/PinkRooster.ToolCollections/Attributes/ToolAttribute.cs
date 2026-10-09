using Microsoft.Extensions.AI;

namespace PinkRooster.ToolCollections;

/// <summary>
/// Specifies that a method should be exposed as an AI function tool within a <see cref="ToolCollection"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public class ToolAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the custom name for the tool. If null or whitespace, the C# method name is used.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the custom description for the tool. If null or whitespace, the method's <see cref="System.ComponentModel.DescriptionAttribute"/> is used, if any.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets whether the tool needs the host's approval before it runs. When set, the tool is an
    /// <see cref="ApprovalRequiredAIFunction"/>: a run that calls it ends with a <see cref="ToolApprovalRequestContent"/>, and the
    /// tool runs only once the host sends the approval back on the same session.
    /// </summary>
    public bool RequiresApproval { get; set; }

    /// <summary>
    /// Gets or sets what the tool does: read, change the agent's own state, edit or execute. A host reads it from the tool with
    /// <see cref="ToolKindExtensions.GetKind"/> to decide which calls to allow without asking. <see cref="ToolKind.None"/>, the
    /// default, declares nothing.
    /// </summary>
    public ToolKind Kind { get; set; }

    /// <summary>
    /// Marks a method as an AI tool using default settings.
    /// </summary>
    public ToolAttribute()
    {
    }

    /// <summary>
    /// Marks a method as an AI tool with a specified description.
    /// </summary>
    /// <param name="description">The description of the tool provided to the AI model.</param>
    public ToolAttribute(string description)
    {
        Description = description;
    }

    /// <summary>
    /// Marks a method as an AI tool with a specified name and description.
    /// </summary>
    /// <param name="name">The custom name for the tool provided to the AI model.</param>
    /// <param name="description">The description of the tool provided to the AI model.</param>
    public ToolAttribute(string name, string description)
    {
        Name = name;
        Description = description;
    }
}
