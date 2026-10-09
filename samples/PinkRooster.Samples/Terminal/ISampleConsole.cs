using PinkRooster.SpectreConsole;

namespace PinkRooster.Samples.Terminal;

/// <summary>What a sample needs from the screen, so its <c>RunAsync</c> runs the same on a terminal and in a test: the <see cref="IAgentConsole"/> of <c>PinkRooster.SpectreConsole</c> and a way to show an error.</summary>
public interface ISampleConsole : IAgentConsole
{
    void WriteError(Exception error);
}
