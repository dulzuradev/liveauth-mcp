using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LiveAuthCore.Tests.Services;

public class WebhookDeliveryWorkerTests
{
    [Fact]
    public async Task ProcessPendingWebhooksAsync_DeliveredWebhook_PostsSignedPayloadAndMarksDelivered()
    {
        const string payload = """{"hello":"world"}""";
        const string secret = "webhook-secret";
        var sentRequests = new List<CapturedRequest>();
        var services = CreateServices(sentRequests, _ => JsonResponse(HttpStatusCode.OK, "ok"));
        var project = CreateProject(webhookUrl: "https://seller.example.com/webhook", webhookSecret: secret);
        var webhook = CreateWebhookEvent(project, payloadJson: payload);
        await SeedAsync(services, project, webhook);
        var worker = CreateWorker(services);

        await worker.ProcessPendingWebhooksAsync(CancellationToken.None);

        var saved = await FindWebhookAsync(services, webhook.Id);
        saved.Status.Should().Be(WebhookEventStatus.Delivered);
        saved.AttemptCount.Should().Be(1);
        saved.LastStatusCode.Should().Be((int)HttpStatusCode.OK);
        saved.LastError.Should().BeNull();
        saved.DeliveredAt.Should().NotBeNull();

        sentRequests.Should().ContainSingle();
        var request = sentRequests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.Url.Should().Be("https://seller.example.com/webhook");
        request.Body.Should().Be(payload);
        request.Headers["X-LiveAuth-Event"].Should().Be(webhook.EventType);
        request.Headers["X-LiveAuth-Event-Id"].Should().Be(webhook.Id.ToString());
        request.Headers["X-LiveAuth-Signature"].Should().Be(ComputeSignature(payload, secret));
    }

    [Fact]
    public async Task ProcessPendingWebhooksAsync_FailedHttpResponse_SchedulesRetry()
    {
        var before = DateTime.UtcNow;
        var services = CreateServices(
            sentRequests: new List<CapturedRequest>(),
            _ => JsonResponse(HttpStatusCode.BadGateway, "upstream down"));
        var project = CreateProject(webhookUrl: "https://seller.example.com/webhook");
        var webhook = CreateWebhookEvent(project);
        await SeedAsync(services, project, webhook);
        var worker = CreateWorker(services);

        await worker.ProcessPendingWebhooksAsync(CancellationToken.None);

        var saved = await FindWebhookAsync(services, webhook.Id);
        saved.Status.Should().Be(WebhookEventStatus.Failed);
        saved.AttemptCount.Should().Be(1);
        saved.LastStatusCode.Should().Be((int)HttpStatusCode.BadGateway);
        saved.LastError.Should().Contain("HTTP BadGateway");
        saved.LastError.Should().Contain("upstream down");
        saved.NextAttemptAt.Should().BeAfter(before);
        saved.DeliveredAt.Should().BeNull();
    }

    [Fact]
    public async Task ProcessPendingWebhooksAsync_ExhaustedPendingEvent_MarksDeadWithoutSending()
    {
        var sentRequests = new List<CapturedRequest>();
        var services = CreateServices(sentRequests, _ => JsonResponse(HttpStatusCode.OK, "ok"));
        var project = CreateProject(webhookUrl: "https://seller.example.com/webhook");
        var webhook = CreateWebhookEvent(project, attemptCount: 5);
        await SeedAsync(services, project, webhook);
        var worker = CreateWorker(services);

        await worker.ProcessPendingWebhooksAsync(CancellationToken.None);

        var saved = await FindWebhookAsync(services, webhook.Id);
        saved.Status.Should().Be(WebhookEventStatus.Dead);
        saved.NextAttemptAt.Should().Be(DateTime.MaxValue);
        sentRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessPendingWebhooksAsync_StaleInProgressEvent_RetriesDelivery()
    {
        var sentRequests = new List<CapturedRequest>();
        var services = CreateServices(sentRequests, _ => JsonResponse(HttpStatusCode.Accepted, "queued"));
        var project = CreateProject(webhookUrl: "https://seller.example.com/webhook");
        var webhook = CreateWebhookEvent(
            project,
            status: WebhookEventStatus.InProgress,
            attemptCount: 1,
            lastAttemptAt: DateTime.UtcNow.AddMinutes(-6),
            nextAttemptAt: DateTime.UtcNow.AddHours(1));
        await SeedAsync(services, project, webhook);
        var worker = CreateWorker(services);

        await worker.ProcessPendingWebhooksAsync(CancellationToken.None);

        var saved = await FindWebhookAsync(services, webhook.Id);
        saved.Status.Should().Be(WebhookEventStatus.Delivered);
        saved.AttemptCount.Should().Be(2);
        saved.LastStatusCode.Should().Be((int)HttpStatusCode.Accepted);
        sentRequests.Should().ContainSingle();
    }

    [Fact]
    public async Task ProcessPendingWebhooksAsync_MissingDestinationUrl_FailsWithoutSending()
    {
        var sentRequests = new List<CapturedRequest>();
        var services = CreateServices(sentRequests, _ => JsonResponse(HttpStatusCode.OK, "ok"));
        var project = CreateProject(webhookUrl: null);
        var webhook = CreateWebhookEvent(project, destinationUrl: null);
        await SeedAsync(services, project, webhook);
        var worker = CreateWorker(services);

        await worker.ProcessPendingWebhooksAsync(CancellationToken.None);

        var saved = await FindWebhookAsync(services, webhook.Id);
        saved.Status.Should().Be(WebhookEventStatus.Failed);
        saved.AttemptCount.Should().Be(1);
        saved.LastStatusCode.Should().BeNull();
        saved.LastError.Should().Be("No webhook URL configured");
        sentRequests.Should().BeEmpty();
    }

    private static WebhookDeliveryWorker CreateWorker(IServiceProvider services)
    {
        return new WebhookDeliveryWorker(
            services,
            NullLogger<WebhookDeliveryWorker>.Instance);
    }

    private static ServiceProvider CreateServices(
        List<CapturedRequest> sentRequests,
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var databaseName = $"WebhookWorkerTests_{Guid.NewGuid():N}";

        return new ServiceCollection()
            .AddDbContext<LiveAuthDbContext>(options =>
                options.UseInMemoryDatabase(
                    databaseName,
                    databaseRoot))
            .AddSingleton<IHttpClientFactory>(
                new StubHttpClientFactory(sentRequests, responder))
            .BuildServiceProvider();
    }

    private static async Task SeedAsync(
        IServiceProvider services,
        Project project,
        WebhookEvent webhook)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        db.Projects.Add(project);
        db.WebhookEvents.Add(webhook);
        await db.SaveChangesAsync();
    }

    private static async Task<WebhookEvent> FindWebhookAsync(
        IServiceProvider services,
        Guid webhookId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        return await db.WebhookEvents.SingleAsync(e => e.Id == webhookId);
    }

    private static Project CreateProject(string? webhookUrl, string? webhookSecret = null)
    {
        return new Project
        {
            Id = Guid.NewGuid(),
            DeveloperId = Guid.NewGuid(),
            Name = "Webhook Project",
            PublicKey = $"la_pk_{Guid.NewGuid():N}",
            SecretKeyHash = "unused",
            WebhookUrl = webhookUrl,
            WebhookSecret = webhookSecret,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            Plan = "free"
        };
    }

    private static WebhookEvent CreateWebhookEvent(
        Project project,
        string payloadJson = """{"event":"test"}""",
        string? destinationUrl = "",
        WebhookEventStatus status = WebhookEventStatus.Pending,
        int attemptCount = 0,
        DateTime? lastAttemptAt = null,
        DateTime? nextAttemptAt = null)
    {
        return new WebhookEvent
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Project = project,
            EventType = "liveauth.test",
            PayloadJson = payloadJson,
            DestinationUrl = destinationUrl,
            CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            NextAttemptAt = nextAttemptAt ?? DateTime.UtcNow.AddSeconds(-1),
            AttemptCount = attemptCount,
            LastAttemptAt = lastAttemptAt,
            Status = status
        };
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string content)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, "text/plain")
        };
    }

    private static string ComputeSignature(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        string Url,
        string Body,
        IReadOnlyDictionary<string, string> Headers);

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly List<CapturedRequest> _sentRequests;
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpClientFactory(
            List<CapturedRequest> sentRequests,
            Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _sentRequests = sentRequests;
            _responder = responder;
        }

        public HttpClient CreateClient(string name)
        {
            name.Should().Be("webhooks");
            return new HttpClient(new StubHttpMessageHandler(_sentRequests, _responder));
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly List<CapturedRequest> _sentRequests;
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpMessageHandler(
            List<CapturedRequest> sentRequests,
            Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _sentRequests = sentRequests;
            _responder = responder;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content == null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var headers = request.Headers
                .ToDictionary(header => header.Key, header => string.Join(",", header.Value));

            _sentRequests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri?.ToString() ?? "",
                body,
                headers));

            return _responder(request);
        }
    }
}
