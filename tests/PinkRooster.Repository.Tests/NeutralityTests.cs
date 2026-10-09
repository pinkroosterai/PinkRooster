using System.Text.Json;
using System.Xml.Linq;

namespace PinkRooster.Repository.Tests;

/// <summary>
/// The libraries stay provider- and UI-neutral: no provider, Azure or console-UI package in any <c>src</c> project's resolved
/// references, and no MCP package outside <c>PinkRooster.ToolCollections.Mcp</c>, the one adapter allowed to know MCP; the OpenAI SDK only in <c>PinkRooster.OpenAI.Reasoning</c>, and Spectre.Console only in <c>PinkRooster.SpectreConsole</c>.
/// The tool collection packages sit below the agent builder: none of them may reference <c>PinkRooster.Agents</c>.
/// </summary>
public sealed class NeutralityTests
{
    private static readonly string[] ForbiddenPrefixes = ["Azure.", "OpenAI", "Spectre."];
    private const string McpPrefix = "ModelContextProtocol";
    private const string McpAdapter = "PinkRooster.ToolCollections.Mcp";
    private const string OpenAiPrefix = "OpenAI";
    private const string OpenAiAdapter = "PinkRooster.OpenAI.Reasoning";
    private const string SpectrePrefix = "Spectre.";
    private const string SpectreAdapter = "PinkRooster.SpectreConsole";
    private static readonly string[] ToolCollectionPackages = ["PinkRooster.ToolCollections", "PinkRooster.ToolCollections.BuiltIn", McpAdapter];

    [Fact]
    public void SourceProjects_ResolveNoProviderOrUiPackage()
    {
        string root = FindRepositoryRoot();
        string[] assetFiles = Directory.GetFiles(Path.Combine(root, "src"), "project.assets.json", SearchOption.AllDirectories);
        Assert.NotEmpty(assetFiles);

        List<string> offenders = [];
        foreach (string assetFile in assetFiles)
        {
            string project = ProjectOf(root, assetFile);
            string[] forbidden = [.. ForbiddenPrefixes.Where(prefix => (project != OpenAiAdapter || prefix != OpenAiPrefix) && (project != SpectreAdapter || prefix != SpectrePrefix)), .. project == McpAdapter ? [] : new[] { McpPrefix }];
            using JsonDocument assets = JsonDocument.Parse(File.ReadAllText(assetFile));
            foreach (JsonProperty library in assets.RootElement.GetProperty("libraries").EnumerateObject())
            {
                string name = library.Name.Split('/')[0];
                if (forbidden.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                {
                    offenders.Add($"{name} in {Path.GetRelativePath(root, assetFile)}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void TheSpectreAdapter_IsTheOnlySourceProjectResolvingSpectre()
    {
        string root = FindRepositoryRoot();
        string[] resolvers =
        [
            .. Directory.GetFiles(Path.Combine(root, "src"), "project.assets.json", SearchOption.AllDirectories)
                .Where(file => JsonDocument.Parse(File.ReadAllText(file)).RootElement.GetProperty("libraries").EnumerateObject()
                    .Any(library => library.Name.StartsWith(SpectrePrefix, StringComparison.OrdinalIgnoreCase)))
                .Select(file => ProjectOf(root, file))
        ];

        Assert.Equal([SpectreAdapter], resolvers);
    }

    [Fact]
    public void ToolCollectionPackages_ReferenceNoAgentPackage()
    {
        string root = FindRepositoryRoot();
        List<string> offenders = [];
        foreach (string assetFile in Directory.GetFiles(Path.Combine(root, "src"), "project.assets.json", SearchOption.AllDirectories))
        {
            if (!ToolCollectionPackages.Contains(ProjectOf(root, assetFile)))
            {
                continue;
            }

            using JsonDocument assets = JsonDocument.Parse(File.ReadAllText(assetFile));
            offenders.AddRange(assets.RootElement.GetProperty("libraries").EnumerateObject()
                .Select(library => library.Name.Split('/')[0])
                .Where(name => name.StartsWith("PinkRooster.Agents", StringComparison.Ordinal))
                .Select(name => $"{name} in {ProjectOf(root, assetFile)}"));
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void TheSpectreAdapter_ReferencesOnlyTheAgentsAndBuiltInProjects()
    {
        string root = FindRepositoryRoot();
        string[] references =
        [
            .. XDocument.Load(Path.Combine(root, "src", SpectreAdapter, $"{SpectreAdapter}.csproj"))
                .Descendants("ProjectReference")
                .Select(reference => Path.GetFileNameWithoutExtension(((string)reference.Attribute("Include")!).Replace('\\', '/')))
                .Order(StringComparer.Ordinal)
        ];

        Assert.Equal(["PinkRooster.Agents", "PinkRooster.ToolCollections.BuiltIn"], references);
    }

    [Fact]
    public void TheToolCollectionPackagesAreCheckedToo()
    {
        string root = FindRepositoryRoot();
        string[] projects = [.. Directory.GetFiles(Path.Combine(root, "src"), "project.assets.json", SearchOption.AllDirectories).Select(file => ProjectOf(root, file))];

        Assert.Contains(McpAdapter, projects);
        Assert.Contains(SpectreAdapter, projects);
        Assert.Contains("PinkRooster.Agents", projects);
        Assert.All(ToolCollectionPackages, package => Assert.Contains(package, projects));
    }

    /// <summary>The project folder directly under <c>src</c> that an assets file belongs to.</summary>
    private static string ProjectOf(string root, string assetFile) =>
        Path.GetRelativePath(Path.Combine(root, "src"), assetFile).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PinkRooster.slnx")))
            {
                return directory.FullName;
            }
        }
        throw new InvalidOperationException("PinkRooster.slnx not found above the test output folder.");
    }
}
