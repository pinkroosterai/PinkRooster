# Microsoft Agent Framework composition reference

This reference focuses on the MAF concepts most relevant to PinkRooster AgentBuilder. APIs evolve; the repository pins versions, so verify current package/source when names or lifecycle details matter.

## Agent composition model

A MAF agent combines:

- an `AIAgent` abstraction;
- a model/chat client;
- instructions;
- tools;
- middleware;
- history;
- context providers;
- session state.

PinkRooster's `AgentBuilder` builds a `ChatClientAgent` and adds its own composition/event/context behavior around it.

## ChatClientAgent

`ChatClientAgent` supports function tools, multi-turn sessions, streaming, structured output on compatible clients, middleware, and context/history providers.

By default MAF wraps the supplied chat client with function-invocation support.

PinkRooster intentionally keeps that default behavior unless its own `ConfigureToolLoop` path supplies the function loop. Do not set `UseProvidedChatClientAsIs` in this repository.

## Sessions

`AgentSession` owns per-conversation state.

A single agent instance can serve multiple independent sessions. Do not store per-conversation state on the agent definition/builder itself.

Create one session per conversation. Do not casually run overlapping turns against the same session; ordering/state semantics are sequential.

Session serialization/history storage are application concerns distinct from the standing prompt.

## ChatHistoryProvider

History answers: **what prior conversation messages should this session retain and supply?**

Use a custom history provider for storage/retrieval policy, not as a replacement for domain memory or RAG.

History can be client-managed or service-managed depending on the provider.

Do not treat a provider/service conversation ID as an authorization boundary.

## AIContextProvider

Context providers proactively contribute dynamic context to an agent run and may maintain session-scoped provider state.

Use cases include:

- memory;
- user/session context;
- retrieval/RAG;
- planning/Todo state;
- agent mode;
- compaction.

Provider instances can be shared across sessions. Put per-session mutable state in MAF's session/provider state mechanism.

### Context versus tool

Use context when the agent should receive information automatically.

Use a tool when the model should fetch information only when relevant.

This distinction matters for cost: proactive context consumes tokens whenever injected, while an on-demand tool costs context only when used.

### Frequency matters

MAF supports context at different pipeline layers. In the pinned MAF version, providers placed in `ChatClientAgentOptions.AIContextProviders` participate at the agent-run context layer, while providers inserted into the chat-client pipeline can participate inside the tool loop.

PinkRooster's `WithContextProvider` populates agent options. ToolCollection context has its own per-model-call client path in normal `Build()`.

When context must refresh after each tool result, verify the exact lifecycle of the current pinned version rather than assuming all provider registration paths are equivalent.

## Tools

MAF supports several tool categories, including function tools and provider-hosted capabilities.

PinkRooster's normal ToolCollection abstraction is for function tools plus their prompt/context contract.

Keep tool sets focused. When one agent accumulates unrelated responsibilities and many overlapping tools, consider splitting capabilities or using agent-as-tool/workflow composition.

## Tool approvals

Function approvals are enforcement performed by the host/runtime, not just a prompt convention.

PinkRooster can mark a function approval-required in a ToolCollection or wrap it via `RequireApproval` on one agent.

The application still owns the UI/decision/persistence flow for approvals.

## Structured outputs

When the application needs typed output, MAF supports `RunAsync<T>` and response-format options on compatible `ChatClientAgent` providers.

Prefer this over textual "return JSON" instructions when correctness depends on parsing.

If a response format is permanent for an agent, it can be configured through chat options. If it is request-specific, prefer run options where practical.

Provider capabilities differ. Test the target client/model.

## Middleware layers

MAF has distinct interception layers.

### Agent middleware

Runs around an agent invocation.

Use for run-level concerns such as validation, auditing, output transformation, or orchestration.

PinkRooster adds agent middleware with `AgentBuilder.Use((agent, services) => ...)`, MAF's word and shape; the callback wraps the built agent inside the event layer, and the first one added sits nearest the agent. MAF's own middleware, such as `ToolApprovalAgent`, goes in the same way. Application code that needs its own events uses `AgentEvents.Publish`.

### Function/tool middleware

Runs around function invocation.

Use for cross-cutting tool-call concerns such as policy gates, argument checks, or tool-call telemetry when the framework path supports it.

Do not put a deterministic authorization check into a system prompt when middleware/application code can enforce it.

### Chat-client middleware

Runs around model calls.

Use for logging, telemetry, caching, provider-neutral request/response transformations, and model-call concerns.

PinkRooster exposes this with `ConfigureClient`.

Middleware ordering is significant.

## Function invocation loop

MAF's default ChatClientAgent can automatically invoke function tools.

Customize the loop only for a concrete need such as a bounded maximum iteration count or diagnostic behavior.

Do not create a second competing function-invocation loop accidentally.

## Agents as tools

`agent.AsAIFunction()` lets an outer agent call an inner agent.

Use this for open-ended delegation where the inner agent owns a coherent specialist responsibility.

Key behavior:

- the inner agent has its own instructions/tools;
- by default it does not inherit the outer conversation;
- the outer agent receives the inner agent's final result;
- name/description matter for routing;
- a supplied persistent session makes the inner function stateful and introduces concurrency/lifetime concerns.

Do not use agent-as-tool for a deterministic sequence that normal code/workflow should control.

## Workflows

Use an explicit workflow when execution order and handoffs are deterministic or when different stages need different roles/tools.

This is especially preferable to a prompt chain when each stage needs independent agent configuration.

## Observability

MAF supports OpenTelemetry-style traces/metrics/logging, and PinkRooster exposes its own event stream.

Use production observability for operational metrics and traces. Be cautious with sensitive prompt/tool data in telemetry.

## Evaluation

Current MAF includes agent evaluation APIs and local/model-based evaluators.

For normal repository work:

- use deterministic local assertions for exact tool calls, output shape, and invariants;
- use representative model evaluations for semantic quality;
- repeat nondeterministic cases when consistency matters;
- preserve regressions when a prompt or composition change fixes a real failure.

Do not introduce cloud evaluation dependencies or credentials just to validate a small agent change unless the project already uses them or the user requests them.

## Provider neutrality

PinkRooster `src` projects stay provider-neutral.

Do not add OpenAI-, Azure-, UI-, or MCP-specific package dependencies to `src` merely to configure an agent. Provider/client construction belongs in app/sample integration unless the repository architecture explicitly changes.

Provider-specific ChatOptions can still be supplied by callers through the appropriate client/options layer when the abstraction supports them without violating source neutrality.

## Sources

Microsoft Agent Framework references:

- Agent concepts:
  https://learn.microsoft.com/en-us/agent-framework/concepts/agents/
- Agent pipeline:
  https://learn.microsoft.com/en-us/agent-framework/agents/agent-pipeline
- Context providers:
  https://learn.microsoft.com/en-us/agent-framework/journey/adding-context-providers
- Tools:
  https://learn.microsoft.com/en-us/agent-framework/agents/tools/
- Function tools:
  https://learn.microsoft.com/en-us/agent-framework/agents/tools/function-tools
- Structured outputs:
  https://learn.microsoft.com/en-us/agent-framework/agents/structured-outputs
- Middleware:
  https://learn.microsoft.com/en-us/agent-framework/journey/adding-middleware
- Agents as tools:
  https://learn.microsoft.com/en-us/agent-framework/journey/agents-as-tools
- Evaluation:
  https://learn.microsoft.com/en-us/agent-framework/agents/evaluation

Public docs may describe a newer version than the one pinned in `Directory.Packages.props`; check the pinned package's XML docs under `~/.nuget/packages/microsoft.agents.ai/<version>/lib/net10.0/`.
