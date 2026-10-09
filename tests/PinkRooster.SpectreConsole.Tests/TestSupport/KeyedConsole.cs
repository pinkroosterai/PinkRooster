using System.Collections.Concurrent;
using Spectre.Console;
using Spectre.Console.Rendering;
using Spectre.Console.Testing;

namespace PinkRooster.SpectreConsole.Tests.TestSupport;

/// <summary>
/// A test console whose keyboard a test can type on while the code under test reads it on another thread: the testing console's own
/// input is a plain queue, which two threads may not share.
/// </summary>
internal sealed class KeyedConsole : IAnsiConsole
{
    private readonly TestConsole inner = new TestConsole().EmitAnsiSequences().Interactive();

    public ScriptedKeys Keys { get; } = new();

    /// <summary>What was drawn. Read it when nothing draws any more.</summary>
    public string Output => inner.Output;

    public Profile Profile => inner.Profile;

    public IAnsiConsoleCursor Cursor => inner.Cursor;

    public IAnsiConsoleInput Input => Keys;

    public IExclusivityMode ExclusivityMode => inner.ExclusivityMode;

    public RenderPipeline Pipeline => inner.Pipeline;

    public void Clear(bool home) => inner.Clear(home);

    public void Write(IRenderable renderable) => inner.Write(renderable);

    public void WriteAnsi(Action<AnsiWriter> action) => inner.WriteAnsi(action);
}

/// <summary>Keys a test types, read by whoever has the keyboard: the input line's reader through <see cref="IsKeyAvailable"/>, a prompt through <see cref="ReadKeyAsync"/>.</summary>
internal sealed class ScriptedKeys : IAnsiConsoleInput
{
    private readonly ConcurrentQueue<ConsoleKeyInfo> keys = new();
    private readonly TaskCompletionSource promptWaits = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when a prompt has asked for a key and found none: the prompt is open and has the keyboard.</summary>
    public Task PromptWaits => promptWaits.Task;

    public void Type(string text)
    {
        foreach (char character in text)
        {
            keys.Enqueue(new ConsoleKeyInfo(character, ConsoleKey.None, shift: false, alt: false, control: false));
        }
    }

    public void Press(ConsoleKey key) => keys.Enqueue(new ConsoleKeyInfo(key == ConsoleKey.Enter ? '\r' : '\0', key, shift: false, alt: false, control: false));

    public void Submit(string line)
    {
        Type(line);
        Press(ConsoleKey.Enter);
    }

    public bool IsKeyAvailable() => !keys.IsEmpty;

    public ConsoleKeyInfo? ReadKey(bool intercept) => keys.TryDequeue(out ConsoleKeyInfo key) ? key : null;

    public async Task<ConsoleKeyInfo?> ReadKeyAsync(bool intercept, CancellationToken cancellationToken)
    {
        while (true)
        {
            if (keys.TryDequeue(out ConsoleKeyInfo key))
            {
                return key;
            }
            promptWaits.TrySetResult();
            await Task.Delay(5, cancellationToken);
        }
    }
}
