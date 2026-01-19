namespace LiveAuthCore.Services;

using System.Security.Cryptography;
using System.Text;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using Microsoft.EntityFrameworkCore;

public class WebhookDeliveryWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebhookDeliveryWorker> _logger;
    private readonly IConfiguration _config;

    private const int MaxAttempts = 5;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    public WebhookDeliveryWorker(
        IServiceProvider services,
        IHttpClientFactory httpClientFactory,
        ILogger<WebhookDeliveryWorker> logger,
        IConfiguration config)
    {
        _services = services;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WebhookDeliveryWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // shutting down
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing webhook events batch.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }

        _logger.LogInformation("WebhookDeliveryWorker stopped.");
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();

        var now = DateTime.UtcNow;

        var events = await db.WebhookEvents
            .Include(e => e.Project)
            .Where(e =>
                (e.Status == WebhookEventStatus.Pending || e.Status == WebhookEventStatus.Delivering) &&
                e.NextAttemptAt <= now &&
                e.AttemptCount < MaxAttempts)
            .OrderBy(e => e.NextAttemptAt)
            .ThenBy(e => e.CreatedAt)
            .Take(20)
            .ToListAsync(ct);

        if (!events.Any())
            return;

        var client = _httpClientFactory.CreateClient("webhooks");

        foreach (var evt in events)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                await DeliverEventAsync(db, client, evt, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error delivering webhook event {EventId}", evt.Id);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task DeliverEventAsync(
        LiveAuthDbContext db,
        HttpClient client,
        WebhookEvent evt,
        CancellationToken ct)
    {
        var project = evt.Project;

        if (string.IsNullOrWhiteSpace(project.WebhookUrl))
        {
            // No longer has a webhook URL – mark as dead
            evt.Status = WebhookEventStatus.Dead;
            evt.LastError = "Project has no webhook URL configured.";
            evt.LastAttemptAt = DateTime.UtcNow;
            return;
        }

        evt.Status = WebhookEventStatus.Delivering;
        evt.AttemptCount++;
        evt.LastAttemptAt = DateTime.UtcNow;

        var body = evt.PayloadJson;
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        var secret = project.WebhookSecret ?? _config["Webhooks:DefaultSecret"];
        string? signatureHeader = null;

        if (!string.IsNullOrEmpty(secret))
        {
            var payloadToSign = $"{ts}.{body}";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var sig = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadToSign));
            var sigHex = Convert.ToHexString(sig).ToLowerInvariant();
            signatureHeader = $"sha256={sigHex}";
        }

        var request = new HttpRequestMessage(HttpMethod.Post, project.WebhookUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        // LiveAuth standard headers for webhook requests
        request.Headers.Add("X-LiveAuth-Event", evt.EventType);
        request.Headers.Add("X-LiveAuth-Project-Id", project.Id.ToString());
        request.Headers.Add("X-LiveAuth-Timestamp", ts);

        if (signatureHeader != null)
        {
            request.Headers.Add("X-LiveAuth-Signature", signatureHeader);
        }

        var response = await client.SendAsync(request, ct);
        evt.LastStatusCode = (int)response.StatusCode;

        if (response.IsSuccessStatusCode)
        {
            evt.Status = WebhookEventStatus.Delivered;
            evt.LastError = null;
        }
        else
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            evt.LastError = errorBody;

            if (evt.AttemptCount >= MaxAttempts)
            {
                evt.Status = WebhookEventStatus.Dead;
            }
            else
            {
                evt.Status = WebhookEventStatus.Pending;
                evt.NextAttemptAt = DateTime.UtcNow + ComputeBackoff(evt.AttemptCount);
            }

            _logger.LogWarning(
                "Webhook delivery failed for event {EventId}, project {ProjectId}, status {StatusCode}, attempt {Attempt}",
                evt.Id, project.Id, evt.LastStatusCode, evt.AttemptCount);
        }
    }

    private static TimeSpan ComputeBackoff(int attempt)
    {
        // 1st fail: 1 min, 2nd: 5 min, 3rd: 15 min, 4+: 1 hr
        return attempt switch
        {
            1 => TimeSpan.FromMinutes(1),
            2 => TimeSpan.FromMinutes(5),
            3 => TimeSpan.FromMinutes(15),
            _ => TimeSpan.FromHours(1)
        };
    }
}
