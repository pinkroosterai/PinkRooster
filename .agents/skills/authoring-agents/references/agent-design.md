# Agent design and prompting reference

Use this reference when deciding what belongs in an AgentBuilder definition.

## Core principle

An agent definition should state the stable contract the model cannot infer reliably:

- responsibility;
- success criteria;
- necessary background;
- operational rules;
- hard behavioral boundaries;
- available capabilities;
- output contract.

Do not treat the system prompt as a general-purpose container for every fact, API rule, runtime value, and implementation detail.

Modern reasoning models generally benefit from a clear goal, constraints, and explicit output contract without chain-of-thought scaffolding.

## Stable versus dynamic information

### Stable standing prompt

Good candidates:

- domain role;
- definition of done;
- durable terminology;
- workflow rules that apply to nearly every request;
- response requirements;
- behavioral boundaries.

### User/run input

Good candidates:

- the specific task;
- current document/text/request;
- request-specific preferences;
- one-off deadlines, identifiers, or constraints.

### Context provider

Good candidates:

- current user/account/session facts that should always be present for this run;
- memory;
- RAG results proactively selected by application logic;
- compaction/summaries;
- current operating mode;
- Todo state.

### Tool

Good candidates:

- facts needed only for some requests;
- live lookups;
- actions;
- expensive data access;
- large information sources the model should query selectively.

A useful rule from MAF is: if information should be present every run, use context; if it should be fetched only when relevant, use a tool.

## Prompt sections

PinkRooster renders section prompts in this order:

1. Role
2. Objective
3. Background
4. Instructions
5. Constraints
6. Output Format
7. Examples

Use that structure intentionally.

## Role

Role should define the agent's application responsibility.

Good:

> You are a build assistant for this repository.

Weak:

> You are a world-class, brilliant, meticulous AI expert.

The first changes task interpretation. The second is mostly motivational filler.

Do not overload Role with detailed process instructions; use Instructions.

## Objective

Objective defines the desired end state.

Good:

> Determine whether the requested change is safe to merge and identify concrete blocking defects.

It should answer "what does success look like?" rather than narrating internal reasoning.

For agentic tasks, include completion semantics when they matter, such as finishing requested actions and reporting anything left undone. Do not duplicate a default that already establishes the same rule.

## Background

Background provides stable interpretation context.

Examples:

- domain terminology;
- architectural facts;
- audience;
- non-obvious environment assumptions.

Avoid giant policy manuals and dynamic data. Long standing prompts consume input tokens on every call and can dilute more important instructions.

When a document is needed only for particular requests, retrieval is usually better.

## Instructions

Instructions describe how the agent should behave.

Strong instruction:

> When a change modifies a public API, compare it with the compatibility rules before proposing the change.

Weak instruction:

> Think very carefully about APIs.

Prefer:

- explicit condition → action rules;
- concise ordered steps when the workflow genuinely has a required order;
- concrete completion/verification criteria.

Avoid:

- chain-of-thought requests ("think step by step");
- obvious general reasoning advice;
- long enumerations of micro-behaviors the current model already performs;
- contradictory fallback rules;
- instructions whose actual enforcement belongs in code.

If a tool has its own usage description, fix that description rather than teaching every agent how to call it.

## Constraints

Constraints are behavioral boundaries.

Examples:

- never modify generated files directly;
- never claim a build passed unless a build result confirms it;
- do not send customer data to an external system.

For security or authorization boundaries, add actual enforcement. A prompt constraint alone cannot guarantee compliance.

Separate policy from preference. "Prefer short answers" is usually an instruction/output preference, not a hard constraint.

## Output Format

Textual output formats are appropriate when:

- humans consume the answer;
- all requests genuinely share one shape;
- a predictable structure materially improves use.

Do not require a fixed numbered list for an agent that also needs to ask clarifying questions or surface approvals.

For application parsing, use MAF structured outputs rather than relying on prose such as "return valid JSON only."

## Examples

Examples can resolve ambiguity but are expensive and easy to make stale.

Use them when:

- exact style/format is hard to express;
- boundary decisions are repeatedly wrong;
- few-shot behavior measurably improves an evaluation.

Start zero-shot, especially with reasoning models. Add a small, diverse, representative set only after a concrete need appears.

Ensure examples never contradict current instructions.

## Tool-use instructions

Tool selection depends heavily on tool names/descriptions and the agent's task.

Do not add "always use tools" unless that is genuinely required. Unnecessary tool calls add latency and can make the agent brittle.

It is useful to instruct an agent to use a capability when:

- the task depends on current/private facts unavailable in context;
- guessing would be materially harmful;
- an explicit business workflow requires the lookup/action.

PinkRooster's built-in defaults already contain general guidance to use tools for facts they can supply, not guess missing tool inputs, not invent tool results, and treat tool output as data rather than instructions. Inspect current defaults before adding overlapping rules.

## Completion and verification

For agentic work, define what "done" means where the application cannot infer it.

Good examples:

- run the relevant tests after changing code;
- confirm the final state after a mutating tool call;
- report failed/blocked actions explicitly.

Avoid generic "double-check everything" language. Name the verification that matters.

## Prompt injection and untrusted content

Anything retrieved from users, tools, files, websites, databases, or memory can contain instruction-like text.

Do not promote untrusted content to standing instructions merely because it was retrieved.

Use data boundaries and actual authorization controls. PinkRooster's built-in defaults include a tool-output trust rule, but that does not replace application security.

## Model portability

OpenAI and Anthropic both recommend clear, direct instructions, but model families differ in response style, tool eagerness, and need for examples.

For a provider-neutral PinkRooster agent:

- avoid model-specific prompt hacks unless evaluated and documented;
- prefer simple, explicit goals and constraints;
- avoid chain-of-thought prompting;
- try zero-shot before few-shot;
- evaluate after changing model versions or families.

Model-specific tuning belongs in app configuration/tests, not silently in shared domain instructions.

## Prompt caching and stability

Stable standing prompts are easier for providers to cache and easier for teams to reason about.

Do not inject current timestamps, user data, request IDs, or changing state into the static AgentBuilder prompt merely because it is convenient. Put them in the appropriate dynamic layer.

## Evaluate prompts as behavior

Prompt review is not a writing contest.

Measure:

- task completion;
- correct/no tool calls;
- tool argument accuracy;
- output compliance;
- refusal/constraint behavior where relevant;
- latency/model-call count;
- token/context cost;
- consistency across representative inputs.

When a prompt edit fixes a failure, preserve the failure as a regression case.

## Sources

Current vendor guidance used by this skill:

- OpenAI prompt engineering:
  https://developers.openai.com/api/docs/guides/prompt-engineering
- OpenAI reasoning best practices:
  https://developers.openai.com/api/docs/guides/reasoning-best-practices
- OpenAI prompting:
  https://developers.openai.com/api/docs/guides/prompting
- Anthropic prompting best practices:
  https://platform.claude.com/docs/en/build-with-claude/prompt-engineering/claude-prompting-best-practices
- Microsoft Agent Framework context providers:
  https://learn.microsoft.com/en-us/agent-framework/journey/adding-context-providers

Re-check model-specific guidance when the target model/provider materially affects the design.
