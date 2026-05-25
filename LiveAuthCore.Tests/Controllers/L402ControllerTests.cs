using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LiveAuthCore.Tests.Controllers;

public class L402ControllerTests : IClassFixture<LiveAuthWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly LiveAuthWebApplicationFactory _factory;

    public L402ControllerTests(LiveAuthWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateInvoice_RequiresActiveProjectPublicKey()
    {
        var response = await _client.PostAsync("/api/public/l402/invoice", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ValidatePayment_RejectsDifferentProjectPublicKey()
    {
        var wrongProject = await SeedProjectAsync("la_pk_wrong_l402");
        var invoiceRequest = new HttpRequestMessage(HttpMethod.Post, "/api/public/l402/invoice?destination=agent-1&amountSats=2");
        invoiceRequest.Headers.Add("X-LW-Public", "demo_pk_test");

        var invoiceResponse = await _client.SendAsync(invoiceRequest);
        invoiceResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var invoice = await invoiceResponse.Content.ReadFromJsonAsync<L402InvoiceBody>();
        invoice.Should().NotBeNull();

        var validateRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/public/l402/validate?paymentHash={invoice!.PaymentHash}");
        validateRequest.Headers.Add("X-LW-Public", wrongProject.PublicKey);

        var validateResponse = await _client.SendAsync(validateRequest);

        validateResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateBundleInvoice_BindsBundleToProject()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/public/l402/bundle/invoice")
        {
            Content = JsonContent.Create(new { tier = "starter", agentId = "agent-1" })
        };
        request.Headers.Add("X-LW-Public", "demo_pk_test");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<L402BundleInvoiceBody>();
        body.Should().NotBeNull();
        body!.Bolt11.Should().Be(body.Invoice);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        var bundle = db.L402Bundles.Single(b => b.BundleId == body.BundleId);
        bundle.ProjectId.Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000002"));
        bundle.DeveloperId.Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        bundle.AgentId.Should().Be("agent-1");
    }

    private async Task<Project> SeedProjectAsync(string publicKey)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        var developer = new Developer
        {
            Id = Guid.NewGuid(),
            Email = $"{publicKey}@example.test",
            CreatedAt = DateTime.UtcNow
        };
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = publicKey,
            PublicKey = publicKey,
            SecretKeyHash = $"{publicKey}_secret",
            DeveloperId = developer.Id,
            Developer = developer,
            IsActive = true
        };

        db.Developers.Add(developer);
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project;
    }

    private sealed class L402InvoiceBody
    {
        public string PaymentHash { get; set; } = string.Empty;
    }

    private sealed class L402BundleInvoiceBody
    {
        public string BundleId { get; set; } = string.Empty;
        public string Invoice { get; set; } = string.Empty;
        public string Bolt11 { get; set; } = string.Empty;
    }
}
