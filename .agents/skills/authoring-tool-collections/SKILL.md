---
name: authoring-tool-collections
description: Create, modify, review, and improve PinkRooster ToolCollection classes and their model-facing contracts. Use for Tool methods, names/descriptions, parameter or result types, approvals, collection instructions/constraints, constructor-dependent prompt text, or GetContextAsync. Do not use this skill to implement domain/business logic.
---

# Authoring PinkRooster ToolCollections

Use this skill for contract and scaffolding work around `PinkRooster.ToolCollections.ToolCollection`.

The goal is a small, clear, safe model-facing API. A compiling C# method is not enough: its generated AI-function schema, descriptions, side effects, result contract, approval behavior, and collection-level prompt/context must also be sound.

## Scope

This skill may:

- create a new `ToolCollection`;
- add, remove, rename, or reshape tools in an existing collection;
- improve tool names, descriptions, parameter descriptions, DTOs, result contracts, approvals, instructions, constraints, or context;
- review a collection and report issues without changing it;
- add or update contract-focused tests when useful;
- wire a tool to already-existing application behavior without changing that behavior.

This skill must not invent or implement domain/business logic. If required behavior does not already exist, scaffold the contract and leave an explicit implementation TODO or report the missing dependency.

## Read the repository first

Do not treat this skill's API notes as authoritative. Before editing, read the current versions of:

- `src/PinkRooster.ToolCollections/ToolCollection.cs`
- `src/PinkRooster.ToolCollections/ToolAttribute.cs`
- `src/PinkRooster.ToolCollections/ToolCollectionInstructionAttribute.cs`
- `src/PinkRooster.ToolCollections/ToolCollectionConstraintAttribute.cs`
- the target collection and its model-visible DTOs/tests, when modifying or reviewing;
- one or two nearby collections that solve a similar problem.

Also inspect where the collection is attached to agents when naming collisions, approvals, state lifetime, or context integration may matter.

If repository behavior differs from these references, follow the repository.

Read [references/pinkrooster-toolcollection.md](references/pinkrooster-toolcollection.md) for the current abstraction and [references/microsoft-extensions-ai.md](references/microsoft-extensions-ai.md) when designing signatures or schemas.

## Choose the mode

### Create

Identify:

1. the coherent capability represented by the collection;
2. the user/model actions that need tools;
3. which inputs are genuinely supplied by the model;
4. which state/configuration belongs to the host or constructor;
5. which operations have side effects or require approval;
6. what changing state, if any, belongs in `GetContextAsync`.

Design the tool surface before writing methods.

### Modify

Read the full collection, relevant DTOs, tests, and call sites first. Preserve existing business semantics. Change only the model-facing contract and scaffolding needed for the request.

When a contract change would require business-logic changes, make that dependency explicit instead of fabricating the implementation.

### Review / improve

Begin read-only. Apply [references/review-rubric.md](references/review-rubric.md), prioritize findings that can change tool selection, correctness, safety, context cost, or interoperability, and distinguish:

- contract problems that can be fixed without changing business behavior;
- recommendations that require application/business changes and therefore remain findings.

If the user asked for improvements, make the safe contract-only improvements after the review.

## Design the tool surface

Design tools around recognizable agent actions, not around every public service/API method.

Prefer one tool per distinct intent. Split tools when operations differ materially in:

- trigger or user intent;
- read versus write behavior;
- side effects or reversibility;
- approval requirements;
- input shape;
- result semantics.

Combine operations when the model would otherwise have to perform a deterministic sequence that always belongs together.

Avoid both extremes:

- many tiny CRUD primitives that force unnecessary tool choreography;
- one universal `Execute(action, ...)` tool with unrelated modes and conditionally valid parameters.

Read [references/tool-design.md](references/tool-design.md) for detailed rules and examples.

## Design each tool contract

For every tool, decide these in order.

### 1. Name

Use a stable, concrete action name that distinguishes the tool from its siblings.

Names must be unique ignoring case within a collection and should be checked for collisions with other tools used by the same agent.

Do not rely on C# overloads as separate model-facing tools.

Important: `[Tool("text")]` means **description**, not name. To set a name, use the two-argument form or named properties.

### 2. Tool description

A description should let a model answer:

- What does this tool accomplish?
- When should I use it?
- When should I not use it, if confusion with another tool is plausible?
- What material side effects, limits, or irreversibility matter?
- What does the result contain or mean?

Keep it specific and operational. Do not fill it with collection-wide policy or static environment facts.

### 3. Model-visible parameters

Expose only values the model genuinely needs to choose or supply.

Prefer:

- primitives for simple values;
- enums for finite choices;
- nullable/optional parameters only when omission has a clear meaning;
- arrays/lists for natural repeated values;
- purpose-built records/classes for structured input;
- `CancellationToken` for asynchronous work.

Avoid:

- `object` / `dynamic`;
- free-form dictionaries when a stable shape exists;
- magic strings for finite choices;
- multiple booleans that can create invalid combinations;
- credentials, host configuration, tenant/project identity, or other values already known by the application.

Describe every non-obvious model-visible parameter with `[Description]`. Describe nested DTO properties with `[property: Description(...)]` or the equivalent property attribute.

Treat model-supplied values as untrusted. Business-layer validation and authorization remain necessary even when the generated schema is restrictive.

### 4. Side effects and approval

Classify the operation as read-only or mutating and consider:

- consequence;
- reversibility;
- scope;
- user expectation;
- host policy.

Use `RequiresApproval = true` when the host should approve execution. Do not substitute prompt wording such as "ask first" for an approval boundary.

Do not automatically require approval for every write; make the decision from consequence and policy.

Declare what each tool does with `Kind`: `ToolKind.Read` (changes nothing), `State` (changes only the agent's own state, or starts work whose own calls are answered separately), `Edit` (changes files or other data that outlive the run) or `Execute` (runs a command or code). A host reads it with `tool.GetKind()` to decide which calls to allow without asking, so a tool left at `None` is one the host knows nothing about. Pick the kind from the tool's strongest effect, and never mark a tool `Read` that changes anything outside the agent. The kind is separate from approval: set both where both apply.

A host can let the model start any tool in the background with `AgentBuilder.AllowBackground("ToolName")`; the collection is not changed for it. Two things in a collection decide whether that is safe and useful:

- **Concurrent calls.** Allowing a tool asserts it may run at the same time as other calls on the same collection instance. Say in the collection's XML remarks when it may not, for example because the instance holds one working directory or one open connection.
- **Cancellation.** A background task is stopped through the tool's `CancellationToken`: by the model, by its time limit, or when the run ends. A long-running tool that takes no token, or ignores it, is reported as not having stopped and keeps running unseen.

Do not add a `runInBackground` parameter of your own; the builder adds one with that name and refuses a tool that already has it.

### 5. Result contract

Choose the result for the model's next decision.

Prefer a typed result DTO when the result contains distinct fields, identifiers used by later calls, structured state, or a collection of items.

Use `string` when the result is intrinsically textual or a concise human-readable transcript is the clearest contract.

Do not manually JSON-encode a typed object into a string merely to make it structured.

For potentially large results, design filtering, limits, pagination, summaries, or truncation. Never return unbounded logs or large object graphs by default.

For mutating tools, return enough information to establish what happened. Avoid a no-result `Task` when the agent needs confirmation or an identifier.

Errors that the model can correct should be concise and actionable: state what was invalid and what to change. Do not expose stack traces or secrets.

## Put guidance in the right layer

Use one source of truth for each rule.

- **`[Tool]` description:** behavior and selection guidance for one tool.
- **Parameter/property `[Description]`:** meaning, units, allowed interpretation, or useful examples for one model-supplied field.
- **`[ToolCollectionInstruction]`:** static workflow/environment knowledge spanning the collection.
- **`[ToolCollectionConstraint]`:** static collection-wide policy or hard boundary.
- **`AddInstruction(...)`:** collection-wide instruction that depends on constructor configuration.
- **`AddConstraint(...)`:** collection-wide constraint that depends on constructor configuration.
- **`GetContextAsync(...)`:** live changing state needed for current reasoning.

Do not duplicate the same guidance across layers.

If order between several static instruction/constraint statements matters, keep the ordered statements in one attribute because order among multiple attributes on the same class is not guaranteed.

## Use GetContextAsync sparingly

Override `GetContextAsync(CancellationToken)` only when changing state materially helps the model decide what to do now.

Good candidates include a short current-status summary, current selected scope, or a small state snapshot that changes independently of chat history.

Do not use it for:

- static documentation;
- tool usage guidance already in descriptions;
- rules that belong in instructions or constraints;
- large datasets that should be queried through tools;
- secrets or credentials;
- session/user state accidentally stored on a collection shared by multiple agents or sessions.

Keep context short and start it with a descriptive Markdown heading. Remember that the normal builder can request collection context before every model call, including tool-loop iterations, so repeated context has a recurring token cost.

## Respect the business-logic boundary

When creating a collection:

- define constructor dependencies needed to reach existing behavior;
- define attributes, tool signatures, DTO contracts, optional context override, and contract-focused tests;
- call existing services only when their behavior is already established and the mapping is mechanical;
- otherwise use a conspicuous placeholder or TODO and report it as unresolved.

When modifying a collection:

- preserve method bodies where possible;
- do not silently change application semantics while improving the model-facing API;
- if a better tool contract requires a business-layer change, document the required change instead.

The scaffold in [assets/toolcollection-template.cs](assets/toolcollection-template.cs) is illustrative. Adapt it to local repository conventions; do not copy unnecessary sections.

## Validate the model-facing interface

Compilation is necessary but insufficient.

When feasible:

1. build the affected project;
2. run relevant ToolCollection tests;
3. call `GetAIFunctions()` in a test or inspection path;
4. inspect each generated function's:
   - `Name`
   - `Description`
   - `JsonSchema`
   - `ReturnJsonSchema`
   - approval wrapper when applicable;
5. verify required versus optional fields;
6. verify nested DTO descriptions and enum shapes;
7. verify duplicate names ignoring case;
8. check registration sites for cross-collection collisions;
9. verify `GetContextAsync` content, cost, and state lifetime;
10. confirm no unrelated business behavior changed.

Treat an awkward, ambiguous, or unserializable generated schema as a failed contract even when the C# compiles.

## Finish with a contract report

For create/modify work, report:

- tools added, removed, renamed, or reshaped;
- their read/write and approval behavior;
- model-visible inputs and result contracts;
- instruction/constraint/context changes;
- validation performed;
- unresolved business-logic TODOs or required application changes.

For review-only work, report findings in priority order and explain the concrete agent failure or cost each finding can cause.

Do not claim a scaffolded tool is functional when its business behavior is still unimplemented.
