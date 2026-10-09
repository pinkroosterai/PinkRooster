namespace PinkRooster.ToolCollections.BuiltIn.Tests.TestSupport;

/// <summary>A fresh temporary directory, deleted with its content on dispose.</summary>
public sealed class TempDirectory(string prefix) : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory(prefix).FullName;

    // A killed shell can hold its working directory for a moment on Windows, so the delete retries before giving up.
    public void Dispose()
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Delete(Path, recursive: true);
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (IOException) when (attempt < 50)
            {
                Thread.Sleep(100);
            }
        }
    }
}
