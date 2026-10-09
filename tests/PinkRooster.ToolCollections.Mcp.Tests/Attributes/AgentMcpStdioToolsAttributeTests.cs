namespace PinkRooster.ToolCollections.Mcp.Tests;

public sealed class AgentMcpStdioToolsAttributeTests
{
    [Fact]
    public async Task CreateAsync_RejectsABlankName_NamingTheFix()
    {
        AgentMcpStdioToolsAttribute attribute = new(" ", "npx");

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => attribute.CreateAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("blank name", error.Message);
    }

    [Fact]
    public async Task CreateAsync_RejectsABlankCommand_NamingTheServerAndTheFix()
    {
        AgentMcpStdioToolsAttribute attribute = new("Files", "");

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => attribute.CreateAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("'Files'", error.Message);
        Assert.Contains("blank command", error.Message);
    }

    [Fact]
    public async Task CreateAsync_ForAProgramThatDoesNotExist_Throws()
    {
        AgentMcpStdioToolsAttribute attribute = new("Files", "pinkrooster-no-such-program-4f1c", "--stdio");

        await Assert.ThrowsAnyAsync<Exception>(() => attribute.CreateAsync(TestContext.Current.CancellationToken).AsTask());
    }
}
