# ToolCollection review rubric

Use this for review-only tasks and before finalizing create/modify work.

Prioritize findings by likely effect on:

1. incorrect tool selection or invocation;
2. unsafe/unexpected side effects;
3. invalid or ambiguous generated schema;
4. wrong state/context behavior;
5. excessive context/token cost;
6. interoperability/maintainability.

Do not manufacture findings to fill every section.

## 1. Collection boundary

Check:

- Does the collection represent one coherent capability/domain?
- Has it become a grab bag of unrelated tools?
- Are there tools that always travel together and should be one operation?
- Are there tools whose intents, side effects, approvals, or contracts are different enough that they should be split?
- Is the collection exposing backend/API shape instead of agent tasks?

## 2. Tool inventory and names

For each tool:

- Is the method public and intentionally marked `[Tool]`?
- Is the model-facing name concrete and stable?
- Are names unique ignoring case?
- Could another collection attached to the same agent collide?
- Is a C# overload creating ambiguous intent?
- Is `[Tool("...")]` accidentally being used as a name when it is the one-argument description constructor?

Flag accidental exposure/omission before wording polish.

## 3. Tool routing descriptions

Can a model tell:

- what the tool does;
- when to use it;
- which neighboring tool to use instead when applicable;
- whether it reads or changes state;
- important limitations;
- what its result means?

Look for:

- descriptions that merely restate the method name;
- hidden destructive/irreversible behavior;
- multiple tools with nearly indistinguishable descriptions;
- large collection-wide rules duplicated into every tool description;
- stale result descriptions after return-type changes.

## 4. Input contract

For every model-visible parameter and nested property:

- Is the value actually model-known rather than host-known?
- Is the C# type the narrowest useful representation?
- Would an enum remove magic strings?
- Do nullable/default semantics make sense?
- Can interacting booleans create impossible combinations?
- Are units/formats/bounds clear?
- Is `[Description]` present where the name/type is insufficient?
- Is a complex framework/domain type leaking far more schema than intended?
- Is the schema bounded in depth/cardinality where necessary?

Prefer purpose-built tool DTOs over giant internal domain entities.

## 5. Microsoft.Extensions.AI schema

Inspect `AIFunction.JsonSchema` and `ReturnJsonSchema` when feasible.

Check:

- expected property names;
- required versus optional fields;
- enum representation;
- nested property descriptions;
- collection shapes;
- serializability;
- no accidental `CancellationToken`/host state in the model schema;
- no weak `object`/open dictionary shape without a real need;
- reasonable portability across providers.

Compilation does not prove the AI schema is good.

## 6. Side effects and approval

Classify each tool:

- read-only;
- state-mutating;
- externally consequential.

Then check:

- Is the description explicit about material side effects?
- Does application code enforce authorization/validation?
- Should `RequiresApproval` be set under host policy/consequence?
- Does `Kind` say what the tool really does (`Read` only when it changes nothing; `Edit` or `Execute` for the strongest effect)?
- Is approval already added by the builder?
- Is a prompt instruction being mistaken for an enforcement boundary?
- Is the action's scope bounded?

Do not automatically demand approval for every mutation.

## 7. Result contract

Check:

- Does the result contain what the model needs next?
- Should a structured `string` actually be a typed record?
- Is a typed result bloated with irrelevant backend fields?
- Are important IDs/status fields present?
- Do mutating tools confirm what changed?
- Are list/log/search results bounded?
- Are failure messages actionable?
- Can errors leak internal details or secrets?
- Is JSON manually embedded in a string without a reason?

## 8. Instructions and constraints

Review static and constructor-dependent standing text.

Check:

- tool-specific rules are not misplaced as collection instructions;
- workflow/environment facts use instructions;
- hard host/policy boundaries use constraints;
- constructor-dependent facts use `AddInstruction` / `AddConstraint`;
- static facts are not recomputed in context;
- rules are not duplicated across layers;
- ordered statements are not split among attributes whose order is unspecified.

## 9. GetContextAsync

First ask whether the override is necessary.

If present, check:

- content changes independently of static configuration;
- it materially changes current model decisions;
- it begins with a clear Markdown heading;
- it is short enough to repeat;
- it does not dump data that should be queried through a tool;
- it contains no secrets;
- failures/cancellation follow repository behavior;
- it does not rely on unsafe per-session state stored on a shared collection.

Remember the normal builder can call it before every model call/tool-loop iteration.

## 10. State and concurrency

Because one collection instance can be shared across agents/sessions:

- identify mutable fields;
- determine whether they are intentionally shared;
- check thread safety;
- flag per-user/per-session state;
- check callbacks/dependencies for concurrent invocation assumptions.

Do not "fix" state lifetime by inventing a new business architecture; report the required design change when it exceeds contract scope.

## 11. Business-logic boundary

Verify that a contract-focused change did not:

- invent new domain rules;
- alter data access semantics;
- change authorization;
- introduce hidden retry/transaction behavior;
- fabricate service responses.

Mechanical delegation to existing behavior is fine. Required business changes should be explicit TODOs/findings.

## 12. Tests

Look for tests that make the model-facing contract observable.

Depending on the collection, cover:

- tool discovery;
- resolved names;
- descriptions;
- generated parameter schema;
- return schema;
- duplicate names;
- approval wrapper;
- instruction/constraint composition;
- constructor-dependent standing text;
- context;
- regression around a specific reviewed issue.

Prefer small contract tests over broad implementation tests when the skill did not change business logic.

## Review output

For each meaningful finding, state:

- location/tool;
- problem;
- concrete agent failure, safety issue, or cost it can cause;
- recommended contract-level change;
- whether the change can be made without business-logic work.

Then summarize the collection's overall tool surface without assigning a cosmetic score.
