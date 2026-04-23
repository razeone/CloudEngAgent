using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace CloudEngAgent.Mcp.Server.Tests;

/// <summary>
/// Sanity test: the MCP server boots without any connection strings configured
/// and the <c>/healthz</c> endpoint responds. This confirms the tool/DI graph
/// isn't accidentally requiring a live SQL server at startup.
/// </summary>
public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Mcp:SqlServer:ConnectionStrings:default", string.Empty);
        });
    }

    [Fact]
    public async Task Healthz_ReturnsOk()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(new Uri("/healthz", UriKind.Relative));
        response.IsSuccessStatusCode.Should().BeTrue();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"status\"");
    }
}
