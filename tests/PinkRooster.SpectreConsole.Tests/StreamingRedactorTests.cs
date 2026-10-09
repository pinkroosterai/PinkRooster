namespace PinkRooster.SpectreConsole.Tests;

public sealed class StreamingRedactorTests
{
    private static string Redact(string text, params string[] values) =>
        values.Aggregate(text, (current, value) => current.Replace(value, "[redacted]", StringComparison.Ordinal));

    [Theory]
    [InlineData("The key is sk-secret-key, keep it.")]
    [InlineData("sk-secret-key")]
    [InlineData("sk-secret-keysk-secret-key and sk-secret-ke")]
    [InlineData("nothing to hide")]
    public void ValueSplitAtAnyPoint_IsRedactedWhole(string text)
    {
        string[] values = ["sk-secret-key", "pw"];
        for (int size = 1; size <= text.Length + 1; size++)
        {
            StreamingRedactor redactor = new(values, value => Redact(value, values));
            string streamed = "";
            for (int index = 0; index < text.Length; index += size)
            {
                streamed += redactor.Push(text.Substring(index, Math.Min(size, text.Length - index)));
            }

            Assert.Equal(Redact(text, values), streamed + redactor.Flush());
        }
    }

    [Fact]
    public void WithoutSensitiveValues_NothingIsHeldBack()
    {
        StreamingRedactor redactor = new([], text => text);

        Assert.Equal("abc", redactor.Push("abc"));
        Assert.Equal("", redactor.Flush());
    }
}
