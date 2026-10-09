# Agent classes

An agent class is an agent written as a type. It derives from `DeclaredAgent`, is itself a MAF `AIAgent`, and is built through `AgentBuilder` on first use. Source of truth: `src/PinkRooster.Agents/DeclaredAgent.cs`, `Steps/SteppedAgent.cs`, `Briefs/Attributes/`, and the "Agent classes" section of `docs/PinkRooster.Agents.md`. The runnable examples are `samples/PinkRooster.Samples.AgentClasses` and `samples/PinkRooster.Samples.SteppedAgents`.

## The brief

One attribute per section: `[AgentRole]`, `[AgentObjective]`, `[AgentBackground]`, `[AgentInstruction]`, `[AgentConstraint]`, `[AgentOutputFormat]`, `[AgentExample(input, output)]`.

- Role, objective, background and output format appear once per class; several strings in one attribute are joined with newlines.
- Instructions, constraints and examples may repeat. The order between several of the same attribute is not guaranteed: put lines whose order matters in one attribute.
- On a class hierarchy a single-text section on the derived class replaces the base's; the lists run base first.
- Blank text fails at build with a message that names the class and the attribute.
- Attribute text is a constant. Text that depends on a constructor argument goes in `Configure`.
- Role is required, from an attribute or from `Configure`, unless `Configure` sets a whole system prompt (which cannot be combined with attributes).

## Order and Configure

The order is fixed: brief attributes, the class's own `[Tool]` methods and `GetContextAsync`, then `Configure(AgentBuilder)`. The builder's rule for several sources decides a clash: a single-text section in `Configure` replaces the attribute's, lines come after the attributes'.

`Configure` runs once per instance. Put tool collections, context providers, approvals, client middleware, a name and a description there. A derived class that overrides `Configure` without calling `base.Configure` drops what the base class assigned in `Configure` (the base class's attributes and own tools still count).

## Own tools and own context

- Public methods marked `[Tool]`, inherited ones included, are the agent's tools, named, described and approval-marked as in a `ToolCollection`. A duplicate name fails with the builder's error naming the class.
- `GetContextAsync` follows a tool collection's context rules: asked before every model call, sent ahead of the collections' context, never stored in history.
- One instance serves every session at once. Run state lives in the session; a tool or `GetContextAsync` reads the session of the run in progress from `AIAgent.CurrentRunContext`. A run started without a session gets one.

## Events

`protected override OnEvent(AgentEvent)` observes the class's own events. A host adds handlers on the instance with `agent.OnEvent(...)` and `agent.OnEvent<TEvent>(...)`; each returns an `IDisposable` that removes the handler at once. A handler hears the runs that start after it was added, for every session.

## Steps

Derive from `SteppedAgent` and fill in `MaxTurns` (at least 1; it counts turns per message, an approval in between included) and `NextAsync`. `StartAsync` is optional: override it to set state and enter the first step; otherwise the loop enters a step named `start`. `StepContext` gives the session, the input, `Request`, `LastReply`, `Turn` (turns finished for this message), `Enter`, `Checked`, and `SetState<T>` / `GetState<T>` for the run's state. `NextAsync` returns `NextStep.Stop()` or `NextStep.Send(label, prompt, isFinal)`; `NextStep.Send(prompt)` retries inside the current step. A final step ends the run and `NextAsync` is not called after it; final does not say which reply is the answer, which is always the last turn's. No MAF loop type is public. A `SteppedAgent` holds the builder's one step loop, so a second step loop in its `Configure` fails the build. Use `Use((agent, services) => ...)` in `Configure` for middleware around the whole loop.

## Common mistakes

- Run state in a field (two sessions overwrite each other).
- Constructor-dependent text in an attribute.
- Using a class for a one-off agent that a builder chain states in five lines.
- Overriding `Configure` and expecting the base class's `Configure` to still run.
- Reading `CurrentRunContext.Session` outside a run.
