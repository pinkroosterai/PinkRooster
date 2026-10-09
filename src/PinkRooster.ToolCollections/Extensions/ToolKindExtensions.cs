using Microsoft.Extensions.AI;

namespace PinkRooster.ToolCollections;

/// <summary>Reads the <see cref="ToolKind"/> a tool declares.</summary>
public static class ToolKindExtensions
{
    /// <summary>The <see cref="AITool.AdditionalProperties"/> key the kind is kept under.</summary>
    internal const string Key = "PinkRooster.ToolKind";

    /// <summary>
    /// Returns the kind <paramref name="tool"/> declares: that of its <see cref="ToolAttribute.Kind"/>, or the one a collection of
    /// functions from elsewhere gave it with <see cref="ExternalToolCollection{TSelf}.WithKind"/>. <see cref="ToolKind.None"/> for
    /// a tool that declares none.
    /// </summary>
    /// <remarks>
    /// The kind stays readable on a tool wrapped in a <see cref="DelegatingAIFunction"/>, such as an
    /// <see cref="ApprovalRequiredAIFunction"/>.
    /// </remarks>
    /// <param name="tool">The tool, as a collection's <see cref="ToolCollection.GetAIFunctions"/> or an agent's tool list holds it.</param>
    public static ToolKind GetKind(this AITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return tool.AdditionalProperties.TryGetValue(Key, out object? value) && value is ToolKind kind ? kind : ToolKind.None;
    }
}
