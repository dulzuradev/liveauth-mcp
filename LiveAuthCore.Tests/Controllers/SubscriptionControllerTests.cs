using System.Net;
using System.Net.Http.Json;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LiveAuthCore.Tests.Controllers;

/// <summary>
/// Tests for /api/dev/billing/* endpoints (project subscription billing).
/// </summary>
public class SubscriptionControllerTests : IClassFixture<LiveAuthWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly LiveAuthWebApplicationFactory _factory;

    public SubscriptionControllerTests(LiveAuthWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Subscribe_MissingProject_ReturnsNotFound()
    {
        var response = await _client.PostAsJsonAsync("/api/dev/billing/subscribe", new
        {
            ProjectId = Guid.NewGuid(),
            Plan = "pro"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Subscribe_ValidProject_ReturnsInvoice()
    {
        var project = await SeedProject();

        var response = await _client.PostAsJsonAsync("/api/dev/billing/subscribe", new
        {
            ProjectId = project.Id,
            Plan = "pro"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<CreateSubscriptionInvoiceResponse>();
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.SessionId);
        Assert.NotEmpty(result.Invoice);
        Assert.Equal(50_000L, result.AmountSats);
        Assert.True(result.ExpiresAtUnix > DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task Subscribe_ExistingPendingInvoice_ReusesSession()
    {
        var project = await SeedProject();
        var existing = await SeedSubscription(project.Id, isPaid: false, expiresInMinutes: 10);

        var response = await _client.PostAsJsonAsync("/api/dev/billing/subscribe", new
        {
            ProjectId = project.Id,
            Plan = "pro"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<CreateSubscriptionInvoiceResponse>();
        Assert.NotNull(result);
        Assert.Equal(existing.Id, result.SessionId);
        Assert.Equal(existing.InvoiceBolt11, result.Invoice);
    }

    [Fact]
    public async Task Confirm_NonExistentSession_ReturnsNotPaid()
    {
        var response = await _client.PostAsJsonAsync("/api/dev/billing/confirm", new
        {
            SessionId = Guid.NewGuid()
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ConfirmSubscriptionResponse>();
        Assert.NotNull(result);
        Assert.False(result.Paid);
    }

    [Fact]
    public async Task Confirm_AlreadyPaidSession_ReturnsPaid()
    {
        var project = await SeedProject();
        var session = await SeedSubscription(project.Id, isPaid: true, expiresInMinutes: 10);

        var response = await _client.PostAsJsonAsync("/api/dev/billing/confirm", new
        {
            SessionId = session.Id
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ConfirmSubscriptionResponse>();
        Assert.NotNull(result);
        Assert.True(result.Paid);
    }

    private async Task<Project> SeedProject()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();

        var developer = new Developer
        {
            Id = Guid.NewGuid(),
            Email = $"test-{Guid.NewGuid():N}@liveauth.app",
            CreatedAt = DateTime.UtcNow
        };

        var project = new Project
        {
            Id = Guid.NewGuid(),
            DeveloperId = developer.Id,
            Name = "Billing Test Project",
            PublicKey = $"la_pk_billing_{Guid.NewGuid():N}",
            SecretKeyHash = "unused-in-billing-tests",
            Plan = "free",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        db.Developers.Add(developer);
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        return project;
    }

    private async Task<BillingSubscription> SeedSubscription(Guid projectId, bool isPaid, int expiresInMinutes)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();

        var now = DateTime.UtcNow;
        var subscription = new BillingSubscription
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Plan = "pro",
            AmountSats = 50_000L,
            InvoiceBolt11 = $"lnbc{Guid.NewGuid():N}",
            InvoiceRHash = Guid.NewGuid().ToString("N"),
            IsPaid = isPaid,
            PaidAt = isPaid ? now : null,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(expiresInMinutes)
        };

        db.BillingSubscriptions.Add(subscription);
        await db.SaveChangesAsync();

        return subscription;
    }

    private record CreateSubscriptionInvoiceResponse(Guid SessionId, string Invoice, long AmountSats, long ExpiresAtUnix);
    private record ConfirmSubscriptionResponse(bool Paid, DateTime? ProPaidUntil);
}
