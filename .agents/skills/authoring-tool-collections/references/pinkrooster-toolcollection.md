# PinkRooster ToolCollection reference

This reference describes the repository design verified on 2026-09-25. The source code is authoritative; re-read it before making changes.

## Relevant files

- `src/PinkRooster.ToolCollections/ToolCollection.cs`
- `src/PinkRooster.ToolCollections/ToolAttribute.cs`
- `src/PinkRooster.ToolCollections/ToolCollectionInstructionAttribute.cs`
- `src/PinkRooster.ToolCollections/ToolCollectionConstraintAttribute.cs`
- `src/PinkRooster.ToolCollections/Context/ToolCollectionContext.cs`
- `tests/PinkRooster.ToolCollections.Tests/`
- ready-made examples under `src/PinkRooster.ToolCollections.BuiltIn/`

## Tool discovery

`ToolCollection.GetAIFunctions()` reflects public instance and static methods and exposes only methods marked with `[Tool]`.

The resulting `AIFunction` objects are created once per collection instance and cached.

Implications:

- the method must be public;
- unmarked public methods are not tools;
- model-facing function definitions should be stable for the lifetime of the collection;
- do not expect later mutation of attributes/signatures or configuration to regenerate functions.

## Tool names

The name is:

1. `ToolAttribute.Name` when non-blank;
2. otherwise the C# method name.

Names must be unique ignoring case inside one collection. The builder also checks collisions among tools it can see.

Runtime/context-provider composition can still expose collisions the builder could not know about, so inspect the agent composition when reviewing a collection.

### Attribute overload gotcha

The current constructors are conceptually:

```csharp
[Tool]
[Tool("description")]
[Tool("name", "description")]
```

Therefore:

```csharp
[Tool("GetTicket")]
```

sets the **description** to `GetTicket`; it does not rename the tool.

When naming explicitly, prefer:

```csharp
[Tool("GetTicket", "Returns one ticket by number.")]
```

or named properties where clearer.

## Descriptions

If `ToolAttribute.Description` is non-blank, PinkRooster passes it to `AIFunctionFactory`.

If it is absent/blank, `AIFunctionFactory` may derive the function description from the method's `System.ComponentModel.DescriptionAttribute`.

Avoid maintaining conflicting duplicate descriptions in both places.

Parameter and DTO-property descriptions come from `System.ComponentModel.DescriptionAttribute` through Microsoft.Extensions.AI schema generation.

## Approval

`[Tool(..., RequiresApproval = true)]` wraps the generated function in `ApprovalRequiredAIFunction`.

The host must send approval back through the normal approval flow before the underlying tool runs.

Approval may also be added by the agent builder for a named tool. When reviewing, inspect both the collection attribute and relevant builder configuration before concluding a tool is unattended.

Prompt instructions are not a substitute for host approval.

## Kind

`[Tool(..., Kind = ToolKind.Read)]` stores the kind in the generated function's `AdditionalProperties`; `tool.GetKind()` reads it, also through `ApprovalRequiredAIFunction` and any other `DelegatingAIFunction`. The values are `Read`, `State`, `Edit`, `Execute`, and `None` for a tool that declares nothing.

A function added with `AddTools(...)` has no kind. `ExternalToolCollection.WithKind(kind, names)` gives the named tools one; an MCP collection does that itself for the tools its server marks read-only.

The kind enforces nothing. It is what the host's approval logic reads, so a wrong `Read` is a tool that may run unasked.

## Instructions and constraints

Static collection-wide text uses:

- `[ToolCollectionInstruction(...)]`
- `[ToolCollectionConstraint(...)]`

Attribute text is read base-types first, then derived types.

Constructor-dependent text uses protected:

- `AddInstruction(string)`
- `AddConstraint(string)`

Agents read added text when they are built, so text added later reaches only agents built later.

Blank entries are rejected.

Order between several attributes applied to the same class is not guaranteed. If order matters, put the ordered lines in one attribute.

### Layering rule

Use:

- instructions for collection-wide environment/workflow knowledge;
- constraints for collection-wide boundaries/policy;
- tool description for one tool's selection and behavior;
- parameter description for one field's semantics.

Do not duplicate the same rule across all layers.

## GetContextAsync override

The override is:

```csharp
public override ValueTask<string?> GetContextAsync(CancellationToken cancellationToken)
```

Return `null` when the collection has no useful live context.

With the normal builder, the collection can be queried before every model call, including each tool-loop call. Context is sent for the current request and is not stored as normal session history.

With `ToolCollectionContextProvider`, behavior is per run rather than necessarily per model call. Follow the current code/docs for the integration path being used.

Guidance:

- keep context short;
- start with a meaningful Markdown heading;
- use it for changing state, not static instructions;
- avoid duplicating data easily fetched by a tool;
- never expose credentials/secrets;
- remember repeated context costs tokens repeatedly.

## State lifetime

A `ToolCollection` instance may be shared by every agent and session it is given to.

Do not store per-user, per-conversation, or per-session mutable state on the collection unless the surrounding lifetime guarantees make that safe and intentional.

Prefer:

- immutable constructor configuration;
- thread-safe shared state when truly collection-global;
- per-session state owned by the appropriate agent/session mechanism rather than by the collection instance.

Review methods for concurrency assumptions when the collection owns mutable state.

## Business logic boundary for this skill

ToolCollection authoring should establish the model-facing contract and delegate to existing application behavior.

The skill may:

- inject an existing service;
- call an existing service method when the mapping is mechanical;
- preserve current tool bodies while improving metadata/signatures;
- define DTOs and validation expectations.

It must not invent domain behavior merely to make a scaffold look complete.

## Existing examples

### AskUserQuestionToolCollection

Useful patterns:

- detailed selection and non-selection guidance in the tool description;
- bounded input cardinality;
- `[Description]` on the structured tool parameter;
- `[property: Description]` on nested records;
- actionable `Error: ...` results.

### ShellToolCollection

Useful patterns:

- constructor configuration becomes `AddInstruction` / `AddConstraint` text;
- tool description explains side effects, isolation, timeouts, output behavior, and policy refusal;
- model-visible options are bounded;
- `CancellationToken` participates in execution but not the model schema;
- output size is bounded.

These examples are patterns, not mandatory result formats. In particular, do not default every new tool to `string` when a typed result is clearer.

## Tests worth adding or updating

Depending on the change, test:

- exposed tool names;
- description precedence;
- generated parameter schema;
- generated return schema;
- optional versus required fields;
- nested property descriptions;
- duplicate names ignoring case;
- approval wrapping;
- instruction/constraint inheritance/order behavior;
- constructor-added standing text;
- context content and failure behavior;
- state/concurrency assumptions where relevant.

A contract change should normally have a test that would fail if the model-facing schema regressed.
