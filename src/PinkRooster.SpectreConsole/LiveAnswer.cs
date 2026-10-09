using System.Collections.Concurrent;
using System.Text;
using NTokenizers.Extensions.Spectre.Console;
using NTokenizers.Extensions.Spectre.Console.Styles;
using Spectre.Console;

namespace PinkRooster.SpectreConsole;

/// <summary>An answer drawn as markdown while the model is still writing it: text goes in as it arrives, and <see cref="Close"/> returns when the last of it is on the screen.</summary>
/// <remarks>
/// The renderer reads a stream and redraws tables and code blocks in place with cursor moves, so nothing else may write to the console
/// between the first <see cref="Push"/> and <see cref="Close"/>. <c>WriteMarkdownAsync</c> blocks the thread that calls it until the stream ends,
/// so it is started on the thread pool; closing waits for it, which is safe in a handler that is called on the agent's own thread
/// (<c>research.md § Streaming spike</c> in the planning notes). Trailing blanks are held until more text follows, as <c>WriteAnswer</c> trims them.
/// </remarks>
internal sealed class LiveAnswer(IAnsiConsole console, StreamingRedactor redactor)
{
    private readonly AnswerMarkdownStream fixes = new(console.Profile.Width);
    private readonly StringBuilder blanks = new();
    private BlockingTextStream? stream;
    private Task? render;

    public void Push(string text) => Write(fixes.Push(redactor.Push(text)));

    /// <summary>Ends the answer and waits until it is drawn; the cursor is on a new line afterwards.</summary>
    public void Close()
    {
        Write(fixes.Push(redactor.Flush()) + fixes.Complete());
        if (stream is null)
        {
            return;
        }

        stream.End();
        render!.GetAwaiter().GetResult();
        console.WriteLine();
    }

    private void Write(string text)
    {
        string content = text.TrimEnd();
        if (content.Length == 0)
        {
            blanks.Append(text);
            return;
        }

        string piece = blanks + content;
        blanks.Clear().Append(text[content.Length..]);
        if (stream is null)
        {
            BlockingTextStream started = stream = new BlockingTextStream();
            render = Task.Run(() => console.WriteMarkdownAsync(started, MarkdownStyles.Default, Encoding.UTF8, CancellationToken.None));
        }
        stream.Feed(piece);
    }

    /// <summary>A stream the renderer reads on its own thread, which blocks while there is nothing to read and ends when <see cref="End"/> is called.</summary>
    private sealed class BlockingTextStream : Stream
    {
        private readonly BlockingCollection<byte[]> pieces = [];
        private byte[] current = [];
        private int position;

        public void Feed(string text) => pieces.Add(Encoding.UTF8.GetBytes(text));

        public void End() => pieces.CompleteAdding();

        public override int Read(byte[] buffer, int offset, int count)
        {
            while (position >= current.Length)
            {
                if (!pieces.TryTake(out byte[]? next, Timeout.Infinite))
                {
                    return 0;
                }
                current = next;
                position = 0;
            }

            int copied = Math.Min(count, current.Length - position);
            Array.Copy(current, position, buffer, offset, copied);
            position += copied;
            return copied;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
