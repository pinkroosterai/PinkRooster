using System.Text.Json;
using Microsoft.Extensions.AI;
using PinkRooster.Agents.Permissions;

namespace PinkRooster.Agents.Tests.Permissions;

public sealed class AllowRuleTests
{
    private static FunctionCallContent Shell(object? command) => new("c1", "RunShell", new Dictionary<string, object?> { ["command"] = command });

    [Fact]
    public void ForTool_CoversEveryCallOfTheTool_IgnoringCase_AndNoOtherTool()
    {
        AllowRule rule = AllowRule.ForTool("runshell");

        Assert.True(rule.Matches(Shell("anything; at all")));
        Assert.False(rule.Matches(new FunctionCallContent("c1", "EditFile")));
    }

    [Theory]
    [InlineData("dotnet build", true)]
    [InlineData("dotnet build -c Release", true)]
    [InlineData("  DOTNET BUILD\t-c Release", true)]
    [InlineData("dotnet builder", false)]
    [InlineData("dotnet test", false)]
    [InlineData("git status", false)]
    public void ForPrefix_CoversTextThatStartsWithThePrefixAsWholeWords(string command, bool covered)
    {
        Assert.Equal(covered, AllowRule.ForPrefix("RunShell", "command", "dotnet build").Matches(Shell(command)));
    }

    [Theory]
    [InlineData("dotnet build; rm -rf .")]
    [InlineData("dotnet build && rm -rf .")]
    [InlineData("dotnet build & rm -rf .")]
    [InlineData("dotnet build | tee log")]
    [InlineData("dotnet build `rm -rf .`")]
    [InlineData("dotnet build $(rm -rf .)")]
    [InlineData("dotnet build > /etc/passwd")]
    [InlineData("dotnet build < input")]
    [InlineData("dotnet build\nrm -rf .")]
    [InlineData("dotnet build\r\nrm -rf .")]
    public void ForPrefix_NeverCoversACommandThatCarriesASecondOne(string command)
    {
        Assert.False(AllowRule.ForPrefix("RunShell", "command", "dotnet build").Matches(Shell(command)));
        Assert.False(AllowRule.ForPrefix("RunShell", "command", "dotnet").Matches(Shell(command)));
    }

    [Fact]
    public void ForPrefix_ReadsTheArgumentAsTheToolLoopKeepsIt_AndCoversNothingWithoutText()
    {
        AllowRule rule = AllowRule.ForPrefix("RunShell", "command", "dotnet build");

        Assert.True(rule.Matches(Shell(JsonSerializer.SerializeToElement("dotnet build -c Release"))));
        Assert.False(rule.Matches(Shell(null)));
        Assert.False(rule.Matches(Shell(42)));
        Assert.False(rule.Matches(new FunctionCallContent("c1", "RunShell")));
        Assert.False(rule.Matches(new FunctionCallContent("c1", "RunShell", new Dictionary<string, object?> { ["script"] = "dotnet build" })));
    }

    [Fact]
    public void ForPrefix_WithAPrefixThatChains_ThrowsNamingTheFix()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => AllowRule.ForPrefix("RunShell", "command", "dotnet build && dotnet test"));

        Assert.Contains("ForWholeText", error.Message);
    }

    [Fact]
    public void ForWholeText_CoversExactlyThatText_ChainedOrNot()
    {
        AllowRule rule = AllowRule.ForWholeText("RunShell", "command", "dotnet build && dotnet test");

        Assert.True(rule.Matches(Shell("dotnet build && dotnet test")));
        Assert.True(rule.Matches(Shell(" dotnet build && dotnet test\n")));
        Assert.False(rule.Matches(Shell("dotnet build && dotnet test && rm -rf .")));
        Assert.False(rule.Matches(Shell("dotnet build")));
    }

    [Fact]
    public void ToString_SaysWhatTheRuleCovers()
    {
        Assert.Equal("every EditFile call", AllowRule.ForTool("EditFile").ToString());
        Assert.Equal("RunShell starting with \"dotnet build\"", AllowRule.ForPrefix("RunShell", "command", "dotnet build").ToString());
        Assert.Equal("RunShell with exactly \"make all\"", AllowRule.ForWholeText("RunShell", "command", "make all").ToString());
    }

    [Fact]
    public void BlankValues_Throw()
    {
        Assert.Throws<ArgumentException>(() => AllowRule.ForTool(" "));
        Assert.Throws<ArgumentException>(() => AllowRule.ForPrefix("RunShell", " ", "dotnet"));
        Assert.Throws<ArgumentException>(() => AllowRule.ForPrefix("RunShell", "command", " "));
        Assert.Throws<ArgumentException>(() => AllowRule.ForWholeText("RunShell", "command", ""));
    }
}
