using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace LiveAuthCore.Tests.Controllers;

/// <summary>
/// Tests for the /api/health endpoint.
/// Simple smoke test to verify the API is running.
/// </summary>
public class HealthControllerTests : IClassFixture<LiveAuthWebApplicationFactory>
{
    private readonly HttpClient _client;

    public HealthControllerTests(LiveAuthWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync("/api/health");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_ReturnsHealthyStatus()
    {
        // Act
        var response = await _client.GetAsync("/api/health");
        var content = await response.Content.ReadFromJsonAsync<HealthResponse>();

        // Assert
        Assert.NotNull(content);
        Assert.Equal("healthy", content.Status);
        Assert.NotNull(content.Timestamp);
    }

    [Fact]
    public async Task Health_TimestampIsRecent()
    {
        // Act
        var response = await _client.GetAsync("/api/health");
        var content = await response.Content.ReadFromJsonAsync<HealthResponse>();

        // Assert
        Assert.NotNull(content);
        var age = DateTime.UtcNow - content.Timestamp;
        Assert.True(age.TotalSeconds < 5, "Health timestamp should be within 5 seconds");
    }

    private record HealthResponse(string Status, DateTime Timestamp);
}
