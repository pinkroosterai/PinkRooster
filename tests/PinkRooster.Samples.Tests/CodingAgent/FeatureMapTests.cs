using System.Text.RegularExpressions;

namespace PinkRooster.Samples.Tests.CodingAgent;

/// <summary>The showcase's README says which file holds which feature; the files and the samples it names exist, and the samples index points at it.</summary>
public sealed partial class FeatureMapTests
{
    private static readonly string Root = FindRoot();
    private static readonly string Sample = Path.Combine(Root, "samples", "PinkRooster.Samples.CodingAgent");

    // The rows of the table under "## Feature map": file or folder, what it holds, the library feature, the feature's own sample.
    private static string[][] Rows()
    {
        string readme = File.ReadAllText(Path.Combine(Sample, "README.md"));
        string section = readme[readme.IndexOf("## Feature map", StringComparison.Ordinal)..];
        section = section[..section.IndexOf("\n## ", 3, StringComparison.Ordinal)];
        return [.. section.Split('\n')
            .Where(line => line.StartsWith("| `", StringComparison.Ordinal))
            .Select(line => line.Trim('|').Split('|').Select(cell => cell.Trim()).ToArray())];
    }

    [Fact]
    public void EveryRow_NamesAFileOrFolderThatExists_AndALibraryFeature()
    {
        string[][] rows = Rows();

        // One row per piece of the spec's table.
        Assert.Equal(
            ["Program.cs", "CodingAssistant.cs", "Project/", "Permissions/", "Helpers/", "Commands/", "Sessions/", "Skills/", "Mcp/", "Images/", "Status/", "ModelSettings/", "History/"],
            rows.Select(row => row[0].Trim('`')));
        Assert.All(rows, row =>
        {
            string path = Path.Combine(Sample, row[0].Trim('`').TrimEnd('/'));
            Assert.True(row[0].EndsWith("/`", StringComparison.Ordinal) ? Directory.Exists(path) : File.Exists(path), $"{row[0]} is in the feature map and not in the sample");
            Assert.False(string.IsNullOrWhiteSpace(row[2]), $"{row[0]} names no library feature");
        });
    }

    [Fact]
    public void EveryLinkedSample_Exists_AndEveryFolderOfTheSampleIsInTheMap()
    {
        string[][] rows = Rows();

        string[] links = [.. rows.SelectMany(row => Link().Matches(row[3]).Select(match => match.Groups[1].Value))];
        Assert.NotEmpty(links);
        Assert.All(links, link => Assert.True(File.Exists(Path.GetFullPath(Path.Combine(Sample, link))), $"{link} is linked from the feature map and does not exist"));

        string[] folders = [.. Directory.GetDirectories(Sample).Select(Path.GetFileName).OfType<string>().Where(name => name is not ("bin" or "obj") && !name.StartsWith('.')).Order(StringComparer.Ordinal)];
        Assert.Equal(folders, rows.Select(row => row[0].Trim('`')).Where(name => name.EndsWith('/')).Select(name => name.TrimEnd('/')).Order(StringComparer.Ordinal));
        // No folder carries the name of a kind of type.
        Assert.DoesNotContain(folders, folder => folder is "Extensions" or "Models" or "Options" or "Attributes" or "Builders" or "Events" or "Exceptions");
    }

    [Fact]
    public void TheSamplesIndex_HasTheShowcaseBelowItsTable_AndNoTableRowForIt()
    {
        string index = File.ReadAllText(Path.Combine(Root, "samples", "README.md"));

        int showcase = index.IndexOf("## Showcase", StringComparison.Ordinal);
        Assert.True(showcase > index.LastIndexOf("| [`", StringComparison.Ordinal), "the Showcase section comes after the table of samples");
        Assert.Contains("[`CodingAgent`](PinkRooster.Samples.CodingAgent/README.md)", index[showcase..]);
        Assert.DoesNotContain(index.Split('\n'), line => line.StartsWith("| [`CodingAgent`]", StringComparison.Ordinal));

        // The sample that followed it in the suggested order now follows the one before it.
        string mcp = File.ReadAllText(Path.Combine(Root, "samples", "PinkRooster.Samples.Mcp", "README.md"));
        Assert.Contains("Previous sample: [OtherAgents](../PinkRooster.Samples.OtherAgents/README.md).", mcp);

        // The built-in tools guide still links to the sample's README.
        string guide = File.ReadAllText(Path.Combine(Root, "docs", "PinkRooster.ToolCollections.BuiltIn.md"));
        Assert.Contains("samples/PinkRooster.Samples.CodingAgent/README.md", guide);
        Assert.True(File.Exists(Path.Combine(Sample, "README.md")));
    }

    private static string FindRoot()
    {
        string directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "PinkRooster.slnx")))
        {
            directory = Path.GetDirectoryName(directory) ?? throw new InvalidOperationException("The repository root was not found above the test's directory.");
        }
        return directory;
    }

    [GeneratedRegex(@"\]\(([^)]+)\)")]
    private static partial Regex Link();
}
