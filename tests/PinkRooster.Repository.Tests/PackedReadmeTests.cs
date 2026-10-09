using System.Text.RegularExpressions;

namespace PinkRooster.Repository.Tests;

/// <summary>
/// Keeps the six packed readmes (<c>docs/PinkRooster.*.md</c>, packed as each package's README) inside what NuGet.org renders: no relative link or
/// image, images only from its allow-list of hosts, every code fence tagged with a language. A readme cannot be fixed after a package is published,
/// so a violation has to fail here. The install lines of the root README and the packed readmes carry <c>--prerelease</c> while no stable version exists.
/// </summary>
public sealed partial class PackedReadmeTests
{
    // The hosts NuGet.org renders images and badges from; github.com only for a workflow badge. https://learn.microsoft.com/nuget/nuget-org/package-readme-on-nuget-org
    private static readonly string[] AllowedImageHosts =
    [
        "api.codacy.com", "api.codeclimate.com", "api.dependabot.com", "api.reuse.software", "api.travis-ci.com", "app.codacy.com", "app.deepsource.com",
        "avatars.githubusercontent.com", "badgen.net", "badges.gitter.im", "camo.githubusercontent.com", "caniuse.bitsofco.de", "cdn.jsdelivr.net",
        "cdn.syncfusion.com", "ci.appveyor.com", "circleci.com", "cloudback.it", "codecov.io", "codefactor.io", "coveralls.io", "dev.azure.com",
        "devpod.sh", "flat.badgen.net", "gitlab.com", "i.imgur.com", "img.shields.io", "infragistics.com", "isitmaintained.com",
        "media.githubusercontent.com", "opencollective.com", "raw.github.com", "raw.githubusercontent.com", "snyk.io", "sonarcloud.io",
        "travis-ci.com", "travis-ci.org", "user-images.githubusercontent.com",
    ];

    private static readonly string Root = FindRepositoryRoot();

    public static IEnumerable<TheoryDataRow<string>> PackedReadmes() =>
        Directory.GetFiles(Path.Combine(Root, "docs"), "PinkRooster.*.md").Order().Select(file => new TheoryDataRow<string>(RelativePath(file)));

    [Fact]
    public void ThereAreSixPackedReadmes() =>
        Assert.Equal(6, Directory.GetFiles(Path.Combine(Root, "src"), "*.csproj", SearchOption.AllDirectories).Count(project => !project.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
            && File.ReadAllText(project).Contains("PackagePath=\"/README.md\"", StringComparison.Ordinal)));

    [Theory]
    [MemberData(nameof(PackedReadmes))]
    public void PackedReadme_HasNoRelativeLinkOrImage(string file)
    {
        string[] offenders = [.. Targets(Read(file)).Where(target => !IsAbsolute(target) && !target.StartsWith('#')).Select(target => $"{file}: '{target}'")];

        Assert.True(offenders.Length == 0, $"NuGet.org shows a relative link or image as broken; use an absolute https://github.com/pinkroosterai/PinkRooster/blob/main/... URL:\n{string.Join("\n", offenders)}");
    }

    [Theory]
    [MemberData(nameof(PackedReadmes))]
    public void PackedReadme_ImagesComeFromAnAllowedHost(string file)
    {
        string text = Read(file);
        List<string> offenders = [.. HtmlImage().Matches(text).Select(match => $"{file}: raw HTML image '{match.Value}'")];
        foreach (Match image in MarkdownImage().Matches(text))
        {
            string target = image.Groups["target"].Value;
            if (!Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) || !IsAllowedImage(uri))
            {
                offenders.Add($"{file}: image '{target}'");
            }
        }

        Assert.True(offenders.Count == 0, $"NuGet.org does not render these images:\n{string.Join("\n", offenders)}");
    }

    [Theory]
    [MemberData(nameof(PackedReadmes))]
    public void PackedReadme_TagsEveryCodeFence(string file)
    {
        string[] untagged = [.. OpeningFences(Read(file)).Where(fence => fence.Info.Length == 0).Select(fence => $"{file}:{fence.Line}")];

        Assert.True(untagged.Length == 0, $"A code fence without a language is not highlighted on NuGet.org; tag it (text, csharp, json):\n{string.Join("\n", untagged)}");
    }

    [Fact]
    public void InstallLines_ForPinkRoosterPackages_CarryPrerelease()
    {
        string[] files = ["README.md", .. Directory.GetFiles(Path.Combine(Root, "docs"), "PinkRooster.*.md").Order().Select(RelativePath)];
        string[] offenders =
        [
            .. files.SelectMany(file => Read(file).Split('\n').Select((line, index) => (file, line, number: index + 1)))
                .Where(entry => InstallLine().IsMatch(entry.line) && !entry.line.Contains("--prerelease", StringComparison.Ordinal))
                .Select(entry => $"{entry.file}:{entry.number}: {entry.line.Trim()}"),
        ];

        Assert.True(offenders.Length == 0, $"No stable version exists yet, so `dotnet add package` needs --prerelease:\n{string.Join("\n", offenders)}");
    }

    private static bool IsAbsolute(string target) =>
        target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);

    private static bool IsAllowedImage(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps
        && (AllowedImageHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase)
            || (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && WorkflowBadge().IsMatch(uri.AbsolutePath)));

    /// <summary>Every link and image target outside code fences and code spans.</summary>
    private static IEnumerable<string> Targets(string text) =>
        LinkTarget().Matches(WithoutCode(text)).Select(match => match.Groups["target"].Value);

    private static string WithoutCode(string text)
    {
        List<string> kept = [];
        bool inFence = false;
        foreach (string line in text.Split('\n'))
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
            }
            else if (!inFence)
            {
                kept.Add(CodeSpan().Replace(line, string.Empty));
            }
        }

        return string.Join('\n', kept);
    }

    private static IEnumerable<(int Line, string Info)> OpeningFences(string text)
    {
        bool inFence = false;
        string[] lines = text.Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            if (!lines[index].StartsWith("```", StringComparison.Ordinal))
            {
                continue;
            }

            if (!inFence)
            {
                yield return (index + 1, lines[index][3..].Trim());
            }

            inFence = !inFence;
        }
    }

    private static string Read(string relativePath) => File.ReadAllText(Path.Combine(Root, relativePath)).ReplaceLineEndings("\n");

    private static string RelativePath(string file) => Path.GetRelativePath(Root, file).Replace('\\', '/');

    [GeneratedRegex(@"!?\[[^\]]*\]\((?<target>[^)\s]+)(?:\s+""[^""]*"")?\)")]
    private static partial Regex LinkTarget();

    [GeneratedRegex(@"!\[[^\]]*\]\((?<target>[^)\s]+)(?:\s+""[^""]*"")?\)")]
    private static partial Regex MarkdownImage();

    [GeneratedRegex(@"<img\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlImage();

    [GeneratedRegex("`[^`]*`")]
    private static partial Regex CodeSpan();

    [GeneratedRegex(@"^/[^/]+/[^/]+/workflows/[^/]+/badge\.svg$")]
    private static partial Regex WorkflowBadge();

    [GeneratedRegex(@"dotnet add package PinkRooster\.")]
    private static partial Regex InstallLine();

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
