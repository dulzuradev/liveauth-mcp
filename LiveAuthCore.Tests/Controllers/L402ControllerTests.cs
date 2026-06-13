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
    public async Task CreateInvoice_AppliesMinimumFeeFloor()
    {
        await SetFeeSettingsAsync(invoiceFeeBps: 200, invoiceMinimumFeeSats: 1);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/public/l402/invoice?destination=agent-1&amountSats=1");
        request.Headers.Add("X-LW-Public", "demo_pk_test");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<L402InvoiceBody>();
        body.Should().NotBeNull();
        body!.BaseAmountSats.Should().Be(1);
        body.InvoiceFeeBasisPoints.Should().Be(200);
        body.InvoiceFeeMinimumSats.Should().Be(1);
        body.InvoiceFeeSats.Should().Be(1);
        body.AmountSats.Should().Be(2);
        body.TotalChargedSats.Should().Be(2);
        body.CreditAmountSats.Should().Be(1);
    }

    [Fact]
    public async Task CreateInvoice_AppliesConfiguredBasisPoints()
    {
        await SetFeeSettingsAsync(invoiceFeeBps: 250, invoiceMinimumFeeSats: 1);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/public/l402/invoice?destination=agent-1&amountSats=10000");
        request.Headers.Add("X-LW-Public", "demo_pk_test");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<L402InvoiceBody>();
        body.Should().NotBeNull();
        body!.BaseAmountSats.Should().Be(10_000);
        body.InvoiceFeeBasisPoints.Should().Be(250);
        body.InvoiceFeeSats.Should().Be(250);
        body.AmountSats.Should().Be(10_250);
        body.CreditAmountSats.Should().Be(10_000);
    }

    [Fact]
    public async Task CreateInvoice_ZeroFeeBasisPoints_IgnoresMinimumFee()
    {
        await SetFeeSettingsAsync(invoiceFeeBps: 0, invoiceMinimumFeeSats: 1);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/public/l402/invoice?destination=agent-1&amountSats=1");
        request.Headers.Add("X-LW-Public", "demo_pk_test");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<L402InvoiceBody>();
        body.Should().NotBeNull();
        body!.BaseAmountSats.Should().Be(1);
        body.InvoiceFeeBasisPoints.Should().Be(0);
        body.InvoiceFeeSats.Should().Be(0);
        body.AmountSats.Should().Be(1);
        body.TotalChargedSats.Should().Be(1);
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
        await SetFeeSettingsAsync(bundleMarkupBps: 1500, bundleMinimumFeeSats: 1);
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
        body.BaseAmountSats.Should().Be(50);
        body.MarkupBasisPoints.Should().Be(1500);
        body.MarkupMinimumFeeSats.Should().Be(1);
        body.MarkupSats.Should().Be(7);
        body.AmountSats.Should().Be(57);
        body.TotalChargedSats.Should().Be(57);
        body.CreditAmountSats.Should().Be(50);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        var bundle = db.L402Bundles.Single(b => b.BundleId == body.BundleId);
        bundle.ProjectId.Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000002"));
        bundle.DeveloperId.Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        bundle.AgentId.Should().Be("agent-1");
        bundle.BaseAmountSats.Should().Be(50);
        bundle.MarkupSats.Should().Be(7);
        bundle.TotalChargedSats.Should().Be(57);
        bundle.CreditAmountSats.Should().Be(50);
    }

    [Fact]
    public async Task CreateBundleInvoice_ZeroMarkupBasisPoints_IgnoresMinimumFee()
    {
        await SetFeeSettingsAsync(bundleMarkupBps: 0, bundleMinimumFeeSats: 1);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/public/l402/bundle/invoice")
        {
            Content = JsonContent.Create(new { tier = "starter", agentId = "agent-zero" })
        };
        request.Headers.Add("X-LW-Public", "demo_pk_test");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<L402BundleInvoiceBody>();
        body.Should().NotBeNull();
        body!.BaseAmountSats.Should().Be(50);
        body.MarkupBasisPoints.Should().Be(0);
        body.MarkupSats.Should().Be(0);
        body.AmountSats.Should().Be(50);
        body.CreditAmountSats.Should().Be(50);
    }

    [Fact]
    public async Task BundleInvoice_SnapshotsFeeSettings_WhenSettingsChangeLater()
    {
        await SetFeeSettingsAsync(bundleMarkupBps: 1500, bundleMinimumFeeSats: 1);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/public/l402/bundle/invoice")
        {
            Content = JsonContent.Create(new { tier = "starter", agentId = "agent-snapshot" })
        };
        request.Headers.Add("X-LW-Public", "demo_pk_test");

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<L402BundleInvoiceBody>())!;

        await SetFeeSettingsAsync(bundleMarkupBps: 0, bundleMinimumFeeSats: 0);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        var bundle = db.L402Bundles.Single(b => b.BundleId == body.BundleId);
        bundle.MarkupBasisPoints.Should().Be(1500);
        bundle.MarkupMinimumFeeSats.Should().Be(1);
        bundle.MarkupSats.Should().Be(7);
        bundle.TotalChargedSats.Should().Be(57);
        bundle.CreditAmountSats.Should().Be(50);
    }

    [Fact]
    public async Task BundleClaimAndStatus_RejectDifferentProjectPublicKey()
    {
        var wrongProject = await SeedProjectAsync("la_pk_wrong_l402_bundle");
        var invoiceRequest = new HttpRequestMessage(HttpMethod.Post, "/api/public/l402/bundle/invoice")
        {
            Content = JsonContent.Create(new { tier = "starter", agentId = "agent-1" })
        };
        invoiceRequest.Headers.Add("X-LW-Public", "demo_pk_test");

        var invoiceResponse = await _client.SendAsync(invoiceRequest);
        invoiceResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var bundleInvoice = await invoiceResponse.Content.ReadFromJsonAsync<L402BundleInvoiceBody>();
        bundleInvoice.Should().NotBeNull();

        var claimRequest = new HttpRequestMessage(HttpMethod.Post, "/api/public/l402/bundle/claim")
        {
            Content = JsonContent.Create(new { paymentHash = bundleInvoice!.PaymentHash })
        };
        claimRequest.Headers.Add("X-LW-Public", wrongProject.PublicKey);

        var claimResponse = await _client.SendAsync(claimRequest);

        claimResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var statusRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/public/l402/bundle/status?bundleId={bundleInvoice.BundleId}");
        statusRequest.Headers.Add("X-LW-Public", wrongProject.PublicKey);

        var statusResponse = await _client.SendAsync(statusRequest);

        statusResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task SetFeeSettingsAsync(
        int invoiceFeeBps = 200,
        long invoiceMinimumFeeSats = 1,
        int bundleMarkupBps = 1500,
        long bundleMinimumFeeSats = 1)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        var settings = db.LightningFeeSettings.SingleOrDefault(s => s.Id == 1);
        if (settings == null)
        {
            settings = new LightningFeeSettings { Id = 1 };
            db.LightningFeeSettings.Add(settings);
        }

        settings.InvoiceFeeBasisPoints = invoiceFeeBps;
        settings.InvoiceMinimumFeeSats = invoiceMinimumFeeSats;
        settings.BundleMarkupBasisPoints = bundleMarkupBps;
        settings.BundleMarkupMinimumFeeSats = bundleMinimumFeeSats;
        settings.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
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
        public long AmountSats { get; set; }
        public long BaseAmountSats { get; set; }
        public int InvoiceFeeBasisPoints { get; set; }
        public long InvoiceFeeMinimumSats { get; set; }
        public long InvoiceFeeSats { get; set; }
        public long TotalChargedSats { get; set; }
        public long CreditAmountSats { get; set; }
    }

    private sealed class L402BundleInvoiceBody
    {
        public string BundleId { get; set; } = string.Empty;
        public string Invoice { get; set; } = string.Empty;
        public string Bolt11 { get; set; } = string.Empty;
        public string PaymentHash { get; set; } = string.Empty;
        public long AmountSats { get; set; }
        public long BaseAmountSats { get; set; }
        public int MarkupBasisPoints { get; set; }
        public long MarkupMinimumFeeSats { get; set; }
        public long MarkupSats { get; set; }
        public long TotalChargedSats { get; set; }
        public long CreditAmountSats { get; set; }
    }
}
