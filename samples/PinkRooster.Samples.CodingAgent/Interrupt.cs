namespace PinkRooster.Samples.CodingAgent;

/// <summary>
/// What Ctrl+C means depends on the moment: during a run it stops the run and the prompt returns, at the prompt it ends the program.
/// <c>Main</c> raises it from the console's cancel key; a test raises it itself.
/// </summary>
public sealed class Interrupt : IDisposable
{
    private readonly object gate = new();
    private readonly CancellationTokenSource program = new();
    private CancellationTokenSource? run;

    /// <summary>Cancelled when the interrupt was raised with no run in progress: the program ends.</summary>
    public CancellationToken ProgramToken => program.Token;

    public void Raise()
    {
        lock (gate)
        {
            (run ?? program).Cancel();
        }
    }

    /// <summary>Begins a run: until the returned source is disposed, an interrupt cancels it and not the program.</summary>
    public CancellationTokenSource BeginRun(CancellationToken outer)
    {
        CancellationTokenSource source = CancellationTokenSource.CreateLinkedTokenSource(outer);
        lock (gate)
        {
            run = source;
        }
        return source;
    }

    public void EndRun()
    {
        lock (gate)
        {
            run = null;
        }
    }

    public void Dispose() => program.Dispose();
}
