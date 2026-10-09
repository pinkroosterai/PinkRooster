# Microsoft.Extensions.AI function/schema reference

This repository used `Microsoft.Extensions.AI 10.10.0` when this reference was verified on 2026-09-25.

PinkRooster delegates tool construction to `AIFunctionFactory.Create(method, target, options)`. The practical consequence is: design C# signatures that generate good JSON schemas rather than hand-authoring tool JSON.

Re-check the current package/source when versions change.

## Schema generation

`AIFunctionFactory` derives:

- the input JSON schema from method parameters;
- the return JSON schema from the declared/unwrapped result type.

It uses `System.Text.Json` metadata/serialization rules.

A parameter type that cannot be represented/serialized can fail function creation with a serialization/schema exception.

## Common model-visible parameter types

These are normally suitable when their schema is clear:

- `string`
- integral/floating numeric types
- `bool`
- enums
- nullable primitives/enums
- arrays
- `List<T>` / `IReadOnlyList<T>` and similar serializable collections
- records/classes with serializable properties
- nested combinations of the above.

Do not interpret this as a small whitelist: MEAI can represent many STJ-serializable .NET types. The relevant question for a tool is whether the generated schema is portable and easy for a model to populate correctly.

## Special parameters excluded from the normal model schema

### CancellationToken

`CancellationToken` is supplied from `AIFunction.InvokeAsync` and is not included in the generated tool parameter schema.

Use it normally for asynchronous/cancellable work.

### IServiceProvider

By default, `IServiceProvider` is sourced from `AIFunctionArguments.Services` and excluded from the model schema.

For ToolCollections, prefer constructor injection for normal dependencies because it makes trusted host state explicit and keeps method signatures focused. Use `IServiceProvider` only when dynamic service resolution is genuinely needed.

### AIFunctionArguments

By default, an `AIFunctionArguments` parameter is bound to the invocation arguments object itself and excluded from the generated schema.

This is an advanced escape hatch. Do not use it merely to avoid designing a proper typed tool contract.

## Parameter descriptions

MEAI schema generation reads `System.ComponentModel.DescriptionAttribute` for parameters.

Example:

```csharp
public Task<TicketResult> GetTicket(
    [Description("Ticket number such as PR-123.")]
    string number,
    CancellationToken cancellationToken = default)
```

For properties in model-visible records/classes, use property-level descriptions:

```csharp
public sealed record TicketSearch(
    [property: Description("Words that should appear in title or body.")]
    string Query);
```

Inspect the actual generated schema after introducing complex DTOs.

## Required and optional inputs

C# nullability/defaults influence generated schema and invocation behavior.

Design omission deliberately:

- required non-null parameter when the model must provide a value;
- nullable/defaulted parameter when omission has a useful semantic meaning.

Do not make everything nullable simply to avoid validation failures; that moves ambiguity into runtime behavior.

Provider strict-schema modes can impose additional requirements. PinkRooster's current `ToolAttribute` abstraction does not expose all `AIFunctionFactoryOptions` or provider-specific strictness knobs, so favor simple portable shapes.

## Result types

### Typed values

For ordinary `T` results, MEAI serializes the result and derives a `ReturnJsonSchema`.

For:

- `Task<T>`
- `ValueTask<T>`

the result schema is derived from the unwrapped `T`.

Typed results are appropriate when the model needs structured fields.

### No-result methods

For:

- `void`
- `Task`
- `ValueTask`

there is no return schema.

Use these only when the agent truly needs no confirmation/data. For side-effecting tools it is often better to return a compact result containing status and any identifier/current state the model needs.

### AIContent special case

Return values declared as `AIContent`, derived `AIContent`, or compatible collections of `AIContent` are special-cased and can be returned without ordinary JSON serialization so chat clients can handle rich content.

Use this intentionally; do not choose `AIContent` merely to bypass a difficult schema.

## Loose JSON types

`JsonElement`, `JsonDocument`, and `JsonNode` can participate in marshaling, but they generally weaken the contract.

Prefer a typed record/class unless the domain is genuinely open-ended JSON.

Similarly, avoid `Dictionary<string, object>` as a routine input contract.

## Untrusted arguments

Microsoft's `AIFunctionFactory` documentation explicitly warns that values supplied through `AIFunctionArguments` generally originate from an AI service and should be considered unvalidated and untrusted.

Consequences:

- enforce authorization outside prompt text;
- validate domain invariants in application/business layers;
- do not trust model-supplied paths, identifiers, query fragments, or commands;
- bind trusted host state through collection instances/dependencies rather than asking the model to provide it.

## AIFunctionFactoryOptions scope in PinkRooster

The current `ToolCollection.CreateFactoryOptions` sets the resolved name and optional description. It does not expose the full factory customization surface for each `[Tool]`.

Do not assume this skill can configure, through `ToolAttribute` alone:

- custom parameter binding;
- custom serializer options;
- custom result marshaling;
- result-schema exclusion;
- provider-specific strict mode.

If a task genuinely requires those capabilities, first determine whether PinkRooster's core abstraction must be extended. Treat that as a framework change, not as something to fake in a ToolCollection.

## Provider portability

OpenAI, Anthropic, and other providers support JSON-schema-shaped function tools but may differ in strictness and supported schema features.

For portable ToolCollections:

- prefer simple objects, arrays, enums, scalars, and nullable fields;
- avoid clever polymorphism or deeply recursive types;
- keep property names stable;
- inspect schemas generated by the current MEAI version;
- test against target providers when provider compatibility is a requirement.

Do not add provider-specific schema hacks to a provider-neutral collection without an explicit design decision.

## Sources

Primary/current references:

- Microsoft.Extensions.AI `AIFunctionFactory` API:
  https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.aifunctionfactory
- dotnet/extensions source, `AIFunctionFactory.cs`:
  https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.AI.Abstractions/Functions/AIFunctionFactory.cs
- dotnet/extensions schema generation:
  https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.AI.Abstractions/Utilities/AIJsonUtilities.Schema.Create.cs
- OpenAI function calling:
  https://developers.openai.com/api/docs/guides/function-calling
- Anthropic tool use:
  https://platform.claude.com/docs/en/agents-and-tools/tool-use/overview
