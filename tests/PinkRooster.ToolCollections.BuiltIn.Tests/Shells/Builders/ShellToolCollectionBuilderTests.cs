using PinkRooster.ToolCollections.BuiltIn.Shared;
using PinkRooster.ToolCollections.BuiltIn.Shells;
using PinkRooster.ToolCollections.BuiltIn.Tests.TestSupport;

namespace PinkRooster.ToolCollections.BuiltIn.Tests.Shells;

public sealed class ShellToolCollectionBuilderTests : IDisposable
{
    private readonly TempDirectory temp = new("shellbuilder-test-");

    public void Dispose() => temp.Dispose();

    [Fact]
    public void Build_WithoutSettings_GivesTheSameInstructionsAsTheConstructor()
    {
        ShellToolCollection built = new ShellToolCollectionBuilder().Build();

        Assert.Equal(new ShellToolCollection().Instructions, built.Instructions);
    }

    [Fact]
    public void Build_PutsEverySettingIntoTheCollection()
    {
        ShellToolCollection built = new ShellToolCollectionBuilder()
            .InWorkspace(new Workspace(temp.Path))
            .WithTimeout(TimeSpan.FromSeconds(30))
            .WithMaxTimeout(TimeSpan.FromMinutes(2))
            .WithMaxOutputCharacters(5_000)
            .AllowPrefixes("git", "dotnet")
            .DenyPrefixes("git push")
            .Build();

        Assert.Contains(built.Instructions, text => text.Contains(temp.Path));
        Assert.Contains(built.Instructions, text => text.Contains("stopped after 30 s") && text.Contains("5000 characters"));
        Assert.Contains(built.Constraints, text => text.Contains("'git push'"));
        Assert.Contains(built.Constraints, text => text.Contains("'dotnet'"));
    }

    [Fact]
    public void Build_CalledTwice_GivesIndependentCollections()
    {
        ShellToolCollectionBuilder builder = new ShellToolCollectionBuilder().AllowPrefixes("git");
        ShellToolCollection first = builder.Build();
        ShellToolCollection second = builder.Build();

        Assert.NotSame(first, second);
        Assert.Equal(first.Constraints, second.Constraints);
    }

    [Fact]
    public void Build_DenyPrefixesWithoutAllowPrefixes_ThrowsNamingTheFix()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() => new ShellToolCollectionBuilder().DenyPrefixes("rm").Build());

        Assert.Contains("allowlist", ex.Message);
    }

    [Fact]
    public void Build_MaxTimeoutBelowTimeout_Throws()
    {
        ShellToolCollectionBuilder builder = new ShellToolCollectionBuilder().WithTimeout(TimeSpan.FromMinutes(5)).WithMaxTimeout(TimeSpan.FromMinutes(1));

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Build());
    }

    [Fact]
    public void Methods_RefuseBadArgumentsAtTheCall()
    {
        ShellToolCollectionBuilder builder = new();

        Assert.Throws<ArgumentNullException>(() => builder.InWorkspace(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithTimeout(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithMaxTimeout(TimeSpan.FromSeconds(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithMaxOutputCharacters(0));
        Assert.Throws<ArgumentNullException>(() => builder.AllowPrefixes(null!));
        Assert.Throws<ArgumentException>(() => builder.WithEnvironmentVariable(" ", "x"));
        Assert.Throws<ArgumentException>(() => builder.WithEnvironmentVariable("A=B", "x"));
    }
}
