namespace PinkRooster.ToolCollections.BuiltIn.Tests.TestSupport;

/// <summary>Creates symbolic links for a test, and skips the test where the operating system or the user's privileges do not permit them.</summary>
public static class SymbolicLinks
{
    public static void ToFile(string linkPath, string target) => Create(() => File.CreateSymbolicLink(linkPath, target));

    public static void ToDirectory(string linkPath, string target) => Create(() => Directory.CreateSymbolicLink(linkPath, target));

    private static void Create(Action create)
    {
        try
        {
            create();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Skip($"Symbolic links cannot be created here: {ex.Message}");
        }
    }
}
