using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace PinkRooster.Agents.Steps;

/// <summary>
/// Runs a <see cref="StepProgram"/>: MAF's <see cref="LoopAgent"/> around the builder's agent, with the program deciding each next step.
/// It sits inside the builder's event layer, so one caller run is one event run, whatever the steps.
/// </summary>
/// <remarks>
/// <para>
/// Input made only of tool-approval answers continues the step the approval paused; any other input starts a new message: the
/// session's place is cleared, <c>StartAsync</c> runs, and a step labelled <c>start</c> is entered when it entered none. A run
/// without a session gets one, because the program keeps its place in the session.
/// </para>
/// <para>
/// Every step stays in the session's history. <c>RunAsync</c> returns the last turn's reply; a streamed run yields every turn, with
/// the program's own prompts left out.
/// </para>
/// </remarks>
internal sealed class StepLoopAgent : DelegatingAIAgent
{
    private const string StartLabel = "start";

    private readonly StepProgram program;
    private readonly ConditionalWeakTable<AgentSession, IReadOnlyList<ChatMessage>> inputs;

    public StepLoopAgent(AIAgent innerAgent, StepProgram program, ILoggerFactory? loggerFactory)
        : this(innerAgent, program, loggerFactory, new ConditionalWeakTable<AgentSession, IReadOnlyList<ChatMessage>>())
    {
    }

    private StepLoopAgent(AIAgent innerAgent, StepProgram program, ILoggerFactory? loggerFactory, ConditionalWeakTable<AgentSession, IReadOnlyList<ChatMessage>> inputs)
        : base(new LoopAgent(innerAgent, new StepLoop(program, inputs), LoopOptions(program), loggerFactory))
    {
        this.program = program;
        this.inputs = inputs;
    }

    protected override async Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        ChatMessage[] input = [.. messages];
        session ??= await CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        await BeginAsync(input, session, cancellationToken).ConfigureAwait(false);
        return await base.RunCoreAsync(input, session, options, cancellationToken).ConfigureAwait(false);
    }

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ChatMessage[] input = [.. messages];
        session ??= await CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        await BeginAsync(input, session, cancellationToken).ConfigureAwait(false);
        await foreach (AgentResponseUpdate update in base.RunCoreStreamingAsync(input, session, options, cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }
    }

    private async ValueTask BeginAsync(ChatMessage[] input, AgentSession session, CancellationToken cancellationToken)
    {
        inputs.AddOrUpdate(session, input);
        bool answersApprovals = input.Length > 0
            && input.All(message => message.Contents.Count > 0 && message.Contents.All(content => content is ToolApprovalResponseContent));
        if (!answersApprovals || !StepContext.Resume(session))
        {
            StepContext.Clear(session);
            StepContext step = new(session, input, string.Empty);
            await program.StartAsync(step, cancellationToken).ConfigureAwait(false);
            if (!StepContext.HasStep(session))
            {
                step.Enter(StartLabel);
            }
        }
    }

    private static LoopAgentOptions LoopOptions(StepProgram program) => new()
    {
        MaxIterations = program.MaxTurns,
        NonStreamingReturnsLastResponseOnly = true,
        // Every step stays in the session's history; the program's prompts are kept out of the caller's stream only.
        FreshContextPerIteration = false,
        ExcludeOnBehalfOfMessages = true
    };
}
