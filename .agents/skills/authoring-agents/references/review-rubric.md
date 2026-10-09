# AgentBuilder review rubric

Use this for review-only tasks and before finalizing create/modify work.

Prioritize findings by likely effect on:

1. task correctness/completion;
2. safety and side effects;
3. tool/context correctness;
4. session/state isolation;
5. model-call/token/latency cost;
6. output reliability;
7. maintainability and observability.

Do not manufacture findings to fill every category.

## 1. Responsibility and execution shape

Ask:

- Does the agent have one coherent responsibility?
- Is a plain agent sufficient?
- Is a stepped agent or sub-agent adding justified value?
- Should deterministic stages be a workflow instead?
- Should an unrelated specialist be an agent-as-tool?
- Has one agent accumulated unrelated roles or dozens of overlapping tools?

Flag architecture mismatch before prompt wording.

## 2. Role

Check:

- concise application responsibility;
- no motivational/persona filler;
- no procedural overload;
- no contradiction with agent name/description.

Role is behavioral prompt text; Name/Description metadata are not substitutes.

## 3. Objective

Check:

- clear success condition;
- completion semantics where needed;
- outcome rather than private reasoning steps;
- no duplication of default completion guidance unless specialization is necessary.

A vague role with no objective can be acceptable for a trivial agent, but complex agentic work usually benefits from an explicit definition of done.

## 4. Background

Check:

- every fact is stable across requests;
- dynamic user/session/current data is not embedded;
- large reference material is not dumped into every call;
- no secrets;
- facts are actually necessary to interpret tasks.

Move dynamic/optional knowledge to context or tools.

## 5. Instructions

Look for:

- concrete condition/action rules;
- required workflow steps;
- verification requirements;
- explicit tool-use rules only where non-obvious.

Flag:

- "think step by step";
- generic "be careful/best practices";
- duplicated AgentDefaults;
- duplicated ToolCollection/tool descriptions;
- instructions trying to enforce authorization/security;
- contradictory branches;
- stale provider/model-specific hacks.

## 6. Constraints

Check that constraints represent genuine boundaries.

For each high-stakes constraint ask: what enforces it?

If the answer is only "the prompt", recommend application validation, approval, middleware, permission-scoped tools, or another actual control where appropriate.

Do not turn simple style preferences into hard constraints.

## 7. Output contract

Check:

- whether one textual output format fits every request;
- whether structured output should be used instead;
- whether error/clarification/approval states can still be represented;
- whether examples and format instructions agree.

For machine parsing, prefer MAF structured outputs over textual JSON promises.

## 8. Examples

Check:

- examples solve a demonstrated ambiguity;
- they are current and internally consistent;
- edge cases are represented when needed;
- they do not teach unintended incidental patterns;
- their permanent token cost is justified.

Remove decorative or redundant examples.

## 9. Defaults and raw prompt

Check:

- whether `AgentDefaults` are intentionally retained/replaced;
- `WithoutDefaults` has a specific reason;
- agent factories pass shared `AgentDefaults` through `WithDefaults` (there is no process-wide `Current`);
- tests do not create global-default races;
- `WithSystemPrompt` is truly needed.

If raw prompt is used with ToolCollections, explicitly verify the loss of collection standing Instructions/Constraints is intended.

## 10. Tools and ToolCollections

Check:

- capability set matches responsibility;
- tool names are unique ignoring case;
- no unrelated tools "just in case";
- one-off `WithTool` versus ToolCollection is deliberate;
- agent instructions are not compensating for weak tool definitions;
- ToolCollection context/standing text is expected;
- agent-as-tool delegation is clearly described.

Use the ToolCollection skill for contract redesign.

## 11. Approvals and side effects

For each consequential function:

- is approval intrinsic to the tool or specific to this agent?
- is `RequiresApproval` / `RequireApproval` used at the right layer?
- does the application implement the approval response flow?
- is a prompt "ask first" rule being mistaken for enforcement?
- does plan/review mode accidentally get treated as a permission boundary?

Remember `RequireApproval` only wraps function tools the builder knows about.

## 12. Dynamic context

Check:

- context belongs in a provider instead of static Background;
- proactive context is actually needed every run;
- large/optional data should instead be fetched via a tool;
- provider order is intentional;
- per-session state lives in session/provider state, not shared fields;
- freshness requirements match the registration layer.

With ToolCollections, distinguish normal `Build()` per-model-call collection context from `BuildOptions()` context-provider behavior.

## 13. History and sessions

Check:

- history provider matches persistence needs;
- one session represents one conversation;
- session state is not shared across users;
- the same session is not intentionally run concurrently;
- service-side IDs are not authorization;
- agent configuration is compatible with serialized/resumed sessions.

Do not confuse long-term semantic memory with chat-history storage.

## 14. ConfigureChatOptions

Check whether a first-class builder method already exists.

Flag callbacks that:

- overwrite `Instructions` unexpectedly;
- replace/add `Tools` and bypass builder validation;
- stack conflicting response formats/options;
- depend on callback order without tests.

Use for genuinely uncovered chat settings.

## 15. ConfigureAgentOptions

Because it runs last, inspect carefully.

Flag overwrites of:

- ChatOptions;
- metadata;
- history;
- context providers;

when corresponding builder methods were already used.

Reject `UseProvidedChatClientAsIs` under current repository rules.

## 16. ConfigureClient / middleware

Check:

- concern is actually model-call middleware;
- middleware ordering is deliberate;
- provider neutrality is preserved in `src`;
- logging/telemetry does not leak sensitive content;
- BuildOptions is not expected to carry this configuration.

Do not implement domain workflow in chat-client middleware.

## 17. ConfigureToolLoop

Check:

- there is a concrete need for custom loop behavior;
- maximum iterations are reasonable for the agent;
- detailed errors are safe;
- no duplicate function-invocation layer is being introduced;
- BuildOptions is not used.

Do not add loop customization by default.

## 18. Metadata

Check:

- ID stability has a reason;
- Name is human-readable;
- Description accurately summarizes purpose/capability;
- Description is good enough if the agent becomes a tool;
- no behavioral rule exists only in Description.

## 19. Clone / Apply / shared objects

For `Clone`:

- shared tools/providers/collections are intentionally shareable;
- mutable shared state is thread-safe and correctly scoped.

For `Apply`:

- shared helper is auditable;
- no hidden capability/approval changes surprise callers;
- ordering with later builder calls is understood.

## 20. Events and logging

Check:

- `OnEvent` is needed for UI/application behavior rather than generic production telemetry;
- handlers are fast;
- handlers are thread-safe;
- handler failure behavior is understood;
- logger factory is configured when failure logging is desired.

For operational tracing, consider MAF/client observability middleware.

## 21. Build versus BuildOptions

Verify `BuildOptions()` is not used when the definition requires:

- agent middleware (`Use`);
- PinkRooster events;
- ConfigureClient;
- ConfigureToolLoop;
- exact normal ToolCollection context frequency.

Do not accept "both compile" as semantic equivalence.

## 22. Tests and evaluations

Look for:

- prompt composition tests where relevant;
- expected tool/no-tool cases;
- approval cases;
- context/history/session cases;
- output-format/structured-output cases;
- streaming when used;
- callback order regressions;
- representative semantic eval cases.

A prompt wording change intended to fix a behavior should gain a regression case for that behavior when practical.

## Review output

For every material finding state:

- location/builder call;
- problem;
- concrete failure, safety issue, or cost;
- recommended change;
- whether it is agent-composition-only or requires tool/provider/business work.

Then summarize the resulting agent contract without assigning a cosmetic score.

## Agent classes

- Run state kept in a field instead of the session.
- Constructor-dependent text in an attribute.
- A class where a builder chain would do, or a builder chain copied into several places that should be one class.
- `Configure` overridden without `base.Configure` while a base class assigns tools there.
