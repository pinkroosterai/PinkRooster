# Tool design for AI agents

Use this reference when deciding what tools to expose and how to describe them.

## Principle: design for a nondeterministic caller

An AI tool is not merely a public method or API endpoint. It is an interface a model must select and populate from natural-language context.

Optimize for:

- obvious selection;
- minimal ambiguity;
- few invalid states;
- bounded context use;
- safe side effects;
- useful next-step information.

## Tool boundaries

### Prefer task-level operations

Good tool boundaries correspond to recognizable intents such as:

- find a ticket matching criteria;
- get one ticket's current state;
- create a draft;
- close a ticket;
- run one bounded command.

Do not expose every repository/service method by default.

### Split materially different intents

Separate tools when their:

- purpose differs;
- side effects differ;
- approval requirements differ;
- parameters differ substantially;
- result contracts differ.

Example:

- `GetTicket(number)`
- `CloseTicket(number)`

should normally remain separate because one is read-only and one mutates state.

### Combine deterministic choreography

If the agent would always need to call A and then B, and there is no meaningful decision between them, consider one higher-level tool.

Example: if an internal lookup ID is never useful to the model and is only required to fetch the requested object, resolve it inside the tool/application layer instead of exposing a two-call dance.

### Avoid universal action switches

Avoid designs such as:

```csharp
Execute(string action, string? id, string? query, bool? force, ...)
```

They create conditionally valid schemas, weak descriptions, and poor approval granularity.

## Tool names

Names should:

- identify a concrete action;
- be stable;
- be distinct from sibling tools;
- avoid internal implementation terminology when user/domain language is clearer.

Do not depend on case differences.

Avoid overloaded C# tool methods whose model-facing distinction exists only in the signature.

## Tool descriptions

Treat the tool description as routing documentation.

A strong description usually communicates:

1. what the tool does;
2. when to choose it;
3. a non-use case when confusion is plausible;
4. side effects or irreversible behavior;
5. important limits;
6. the shape or semantics of the result.

Weak:

> Gets tickets.

Better:

> Finds tickets matching text and optional state filters. Use this when the ticket number is unknown or when several tickets may match; use GetTicket when the exact number is already known. Returns at most 20 summaries with ticket number, title and state.

Do not:

- restate the name without adding semantics;
- hide important side effects;
- put collection-wide host policy into every tool description;
- include large prose tutorials unrelated to selection or invocation.

## Parameter design

A parameter name is not always enough. Describe:

- domain meaning;
- units;
- formatting rules;
- inclusive/exclusive boundaries;
- default/omission semantics;
- examples when they resolve ambiguity.

Prefer types that remove invalid choices:

- enum instead of unconstrained string for a closed vocabulary;
- one enum instead of several conflicting booleans;
- record/object instead of several loosely related string parameters;
- nullable type only when absence is meaningful.

Host-known values should usually not be model inputs. Examples include:

- credentials;
- tenant/account identity already bound to the collection;
- filesystem root configured by the host;
- service endpoints;
- authorization scope;
- environment-specific constants.

Keep trusted state on the collection/dependencies rather than asking the model to echo it.

## Structured DTOs

Use small purpose-built DTOs. Describe model-visible nested properties.

Good:

```csharp
public sealed record TicketSearch(
    [property: Description("Words that should appear in the ticket title or body.")]
    string Query,
    [property: Description("Optional state filter. Leave out to search all states.")]
    TicketState? State = null);
```

Avoid huge domain entities with dozens of fields irrelevant to the model.

Do not expose serialization cycles, polymorphic implementation hierarchies, or framework-owned types unless their generated JSON schema has been inspected and is intentionally model-facing.

## Results

Return what the agent needs to continue, not everything the backend knows.

Prefer typed results for:

- identifiers used by later calls;
- status plus metadata;
- lists of records;
- machine-interpretable success/failure state;
- structured search results.

Use text for:

- command output;
- prose already produced by an external system;
- concise display-oriented summaries where no structured follow-up is needed.

Do not JSON-serialize a DTO manually into `string` just to create structure. Let Microsoft.Extensions.AI serialize typed results.

### Bound large outputs

For lists, logs, search, file contents, traces, and diagnostics, provide one or more of:

- filter/query;
- maximum count;
- pagination/cursor;
- selected fields;
- summary;
- head/tail or middle truncation.

Returning thousands of irrelevant tokens reduces model quality and increases cost.

## Error contracts

Model-correctable errors should tell the caller what to change.

Good:

> Error: state must be Open, Closed, or All; got "Finished".

Poor:

> Error 400.

Distinguish where useful between:

- invalid arguments;
- policy refusal;
- not found;
- conflict/current-state mismatch;
- transient/backend failure.

Do not expose stack traces, internal exception details, credentials, or secrets.

## Side effects, approval, and security

Descriptions help the model behave correctly; they are not a security boundary.

For consequential operations, enforce authorization and validation in application code. Use host approval when appropriate.

Consider approval based on:

- user-visible consequence;
- reversibility;
- financial/security impact;
- scope or blast radius;
- whether the operation is expected from the user's request;
- host policy.

Do not equate "write" with "always approve" or "read" with "always safe". A bulk export can be sensitive; a reversible draft update may be routine.

Treat all model-generated arguments as untrusted input.

## Keep the initial tool surface focused

When an agent has many tools, overlapping names/descriptions make selection harder and consume prompt/schema tokens.

Prefer the smallest set that covers the intended workflows. If a collection becomes a grab bag of unrelated capabilities, split it by coherent domain or responsibility.

## Sources

These principles align with current vendor guidance:

- OpenAI function calling: https://developers.openai.com/api/docs/guides/function-calling
- Anthropic, "Writing tools for agents": https://www.anthropic.com/engineering/writing-tools-for-agents
- Anthropic tool-use docs: https://platform.claude.com/docs/en/agents-and-tools/tool-use/overview

Re-check provider documentation when behavior materially depends on current model/tool schema features.
