
namespace PinkRooster.ToolCollections.Mcp.Tests;

public sealed class AgentMcpHttpToolsAttributeTests
{
    [Theory]
    [InlineData("not a url")]
    [InlineData("/relative/mcp")]
    [InlineData("ftp://mcp.example.com/mcp")]
    [InlineData("")]
    public async Task CreateAsync_RejectsAnEndpointThatIsNotAbsoluteHttp_NamingTheFix(string endpoint)
    {
        AgentMcpHttpToolsAttribute attribute = new("Docs", endpoint);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => attribute.CreateAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("'Docs'", error.Message);
        Assert.Contains("https://mcp.example.com/mcp", error.Message);
    }

    [Fact]
    public async Task CreateAsync_RejectsABlankName_NamingTheFix()
    {
        AgentMcpHttpToolsAttribute attribute = new(" ", "https://mcp.example.com/mcp");

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => attribute.CreateAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("blank name", error.Message);
    }

    [Fact]
    public async Task CreateAsync_ForAnUnreachableServer_ThrowsTheConnectionErrorNamingTheEndpoint()
    {
        AgentMcpHttpToolsAttribute attribute = new("Docs", "http://127.0.0.1:1/mcp");

        Exception error = await Assert.ThrowsAnyAsync<Exception>(() => attribute.CreateAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("http://127.0.0.1:1/mcp", error.Message);
    }
}
