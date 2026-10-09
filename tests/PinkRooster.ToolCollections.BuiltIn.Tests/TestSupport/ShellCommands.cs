namespace PinkRooster.ToolCollections.BuiltIn.Tests.TestSupport;

/// <summary>Commands the shell tests run, written for the default shell of the operating system the tests run on.</summary>
public static class ShellCommands
{
    /// <summary>Prints <paramref name="count" /> lines, "lineNNNNN" each: 10 characters with the line break.</summary>
    public static string NumberedLines(int count) => OperatingSystem.IsWindows()
        ? $"1..{count} | ForEach-Object {{ 'line{{0:D5}}' -f $_ }}"
        : $"i=1; while [ $i -le {count} ]; do printf 'line%05d\\n' $i; i=$((i+1)); done";
}
