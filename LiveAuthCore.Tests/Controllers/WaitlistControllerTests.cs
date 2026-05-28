using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LiveAuthCore.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LiveAuthCore.Tests.Controllers;

public class WaitlistControllerTests : IClassFixture<LiveAuthWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly LiveAuthWebApplicationFactory _factory;

    public WaitlistControllerTests(LiveAuthWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Join_CapturesLead()
    {
        var email = $"{Guid.NewGuid():N}@example.test";

        var response = await _client.PostAsJsonAsync("/api/public/waitlist", new
        {
            email,
            useCase = "I want to charge 1 sat per MCP search call.",
            githubOrTwitter = "github.com/example",
            source = "test"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        var lead = db.WaitlistLeads.Single(l => l.Email == email);
        lead.UseCase.Should().Contain("MCP search");
        lead.GithubOrTwitter.Should().Be("github.com/example");
        lead.Source.Should().Be("test");
    }

    [Fact]
    public async Task Join_UpdatesExistingLeadByEmail()
    {
        var email = $"{Guid.NewGuid():N}@example.test";

        await _client.PostAsJsonAsync("/api/public/waitlist", new
        {
            email,
            useCase = "Original use case"
        });

        var response = await _client.PostAsJsonAsync("/api/public/waitlist", new
        {
            email,
            useCase = "Updated use case",
            githubOrTwitter = "@builder"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        var leads = db.WaitlistLeads.Where(l => l.Email == email).ToList();
        leads.Should().HaveCount(1);
        leads[0].UseCase.Should().Be("Updated use case");
        leads[0].GithubOrTwitter.Should().Be("@builder");
    }

    [Fact]
    public async Task Join_RejectsInvalidEmail()
    {
        var response = await _client.PostAsJsonAsync("/api/public/waitlist", new
        {
            email = "not-an-email",
            useCase = "I build MCP tools"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Join_RequiresUseCase()
    {
        var response = await _client.PostAsJsonAsync("/api/public/waitlist", new
        {
            email = $"{Guid.NewGuid():N}@example.test",
            useCase = ""
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
