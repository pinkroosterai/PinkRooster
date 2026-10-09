using PinkRooster.Agents.Eventing;

namespace PinkRooster.Samples.CodingAgent.Status;

/// <summary>
/// Sums the tokens the model reported, from the agent's events: every model call of a run, those of its helpers included, counts
/// for the run in progress and for the conversation.
/// </summary>
public sealed class UsageMeter
{
    private long run;
    private long session;

    /// <summary>The tokens of the run that ended last, or of the one in progress.</summary>
    public long RunTokens => Interlocked.Read(ref run);

    /// <summary>The tokens since the conversation on screen began.</summary>
    public long SessionTokens => Interlocked.Read(ref session);

    /// <summary>The handler for the agent's events; give it to <c>OnEvent</c>.</summary>
    public void Count(ModelCallCompleted call)
    {
        long tokens = call.Usage?.TotalTokenCount ?? 0;
        Interlocked.Add(ref run, tokens);
        Interlocked.Add(ref session, tokens);
    }

    public void BeginRun() => Interlocked.Exchange(ref run, 0);

    public void BeginSession()
    {
        Interlocked.Exchange(ref run, 0);
        Interlocked.Exchange(ref session, 0);
    }

    /// <summary>The line drawn after each run.</summary>
    public string StatusLine(string model, string mode) =>
        $"{model} · {mode} · run {RunTokens:N0} tokens · session {SessionTokens:N0} tokens";
}
