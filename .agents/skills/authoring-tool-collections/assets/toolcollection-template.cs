// Contract/scaffolding template only.
// Adapt names/types to the repository. Remove sections that are not needed.
// Do not invent business behavior merely to complete this template.

using System.ComponentModel;
using PinkRooster.ToolCollections;

namespace Example;

[ToolCollectionInstruction(
    "Static collection-wide workflow or environment fact.")]
[ToolCollectionConstraint(
    "Static collection-wide host/policy boundary.")]
public sealed class ExampleToolCollection : ToolCollection
{
    private readonly IExampleService service;

    public ExampleToolCollection(IExampleService service, string configuredScope)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));

        // Constructor-dependent standing text only.
        AddInstruction($"This collection is configured for scope '{configuredScope}'.");

        // AddConstraint(...) only for constructor-dependent policy/boundaries.
    }

    [Tool(
        "GetExample",
        "Returns one example by its stable identifier. Use this when the identifier is already known. " +
        "It does not modify state. Returns the identifier, display name and current status.",
        Kind = ToolKind.Read)]
    public Task<ExampleResult> GetExample(
        [Description("Stable example identifier, such as EX-123.")]
        string id,
        CancellationToken cancellationToken = default)
    {
        // If an existing service method has identical established semantics,
        // delegate to it mechanically. Otherwise leave business implementation
        // outside the ToolCollection-authoring change.
        throw new NotImplementedException("Business logic is outside the ToolCollection scaffold.");
    }

    [Tool(
        "UpdateExample",
        "Updates the example's status. Use only when the requested new status is known. " +
        "This changes persistent state and returns the resulting current status.",
        RequiresApproval = true,
        Kind = ToolKind.Edit)]
    public Task<ExampleResult> UpdateExample(
        [Description("Stable example identifier, such as EX-123.")]
        string id,
        [Description("The new status to set.")]
        ExampleStatus status,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Business logic is outside the ToolCollection scaffold.");
    }

    // Override only when short, changing state materially helps the model decide
    // what to do now. Static rules belong in instructions/constraints.
    //
    // public override async ValueTask<string?> GetContextAsync(
    //     CancellationToken cancellationToken)
    // {
    //     return $"## Current example state\n...";
    // }
}

public enum ExampleStatus
{
    Pending,
    Active,
    Closed
}

public sealed record ExampleResult(
    [property: Description("Stable example identifier.")]
    string Id,
    [property: Description("Display name.")]
    string Name,
    [property: Description("Current status after the operation.")]
    ExampleStatus Status);

// Represents already-existing application behavior.
// The ToolCollection-authoring skill does not implement this interface.
public interface IExampleService;
