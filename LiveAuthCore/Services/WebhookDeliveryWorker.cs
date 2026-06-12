using System.Text;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LiveAuthCore.Services;

/// <summary>
/// Background worker that processes webhook delivery queue.
/// </summary>
public class WebhookDeliveryWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<WebhookDeliveryWorker> _logger;
    private const int MaxAttempts = 5;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(10);

    public WebhookDeliveryWorker(
        IServiceProvider services,
        ILogger<WebhookDeliveryWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Webhook delivery worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingWebhooks(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing webhooks");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task ProcessPendingWebhooks(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();

        var now = DateTime.UtcNow;
        var staleInProgressBefore = now.AddMinutes(-5);

        var exhaustedEvents = await db.WebhookEvents
            .Where(e =>
                (e.Status == WebhookEventStatus.Pending || e.Status == WebhookEventStatus.Failed) &&
                e.AttemptCount >= MaxAttempts)
            .ToListAsync(ct);

        foreach (var evt in exhaustedEvents)
        {
            evt.Status = WebhookEventStatus.Dead;
            evt.NextAttemptAt = DateTime.MaxValue;
        }

        if (exhaustedEvents.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        // Get pending webhooks that are due for delivery. Stale InProgress
        // events are included so a crash mid-delivery does not wedge the queue.
        var pendingEvents = await db.WebhookEvents
            .Where(e =>
                ((e.Status == WebhookEventStatus.Pending ||
                  e.Status == WebhookEventStatus.Failed) &&
                 e.AttemptCount < MaxAttempts) ||
                (e.Status == WebhookEventStatus.InProgress &&
                 e.LastAttemptAt != null &&
                 e.LastAttemptAt <= staleInProgressBefore))
            .Where(e => e.NextAttemptAt <= now || e.Status == WebhookEventStatus.InProgress)
            .OrderBy(e => e.CreatedAt)
            .Take(10) // Process 10 at a time
            .Include(e => e.Project)
            .ToListAsync(ct);

        foreach (var evt in pendingEvents)
        {
            await DeliverWebhook(evt, db, ct);
        }
    }

    private async Task DeliverWebhook(WebhookEvent evt, LiveAuthDbContext db, CancellationToken ct)
    {
        var destinationUrl = string.IsNullOrWhiteSpace(evt.DestinationUrl)
            ? evt.Project?.WebhookUrl
            : evt.DestinationUrl.Trim();

        if (evt.Project == null || string.IsNullOrWhiteSpace(destinationUrl))
        {
            evt.Status = WebhookEventStatus.Failed;
            evt.LastError = "No webhook URL configured";
            evt.AttemptCount++;
            evt.LastAttemptAt = DateTime.UtcNow;
            evt.LastStatusCode = null;
            evt.NextAttemptAt = DateTime.UtcNow.AddMinutes(5);
            MarkDeadIfExhausted(evt);
            await db.SaveChangesAsync(ct);
            return;
        }

        evt.AttemptCount++;
        evt.Status = WebhookEventStatus.InProgress;
        evt.LastAttemptAt = DateTime.UtcNow;
        evt.LastStatusCode = null;
        await db.SaveChangesAsync(ct);

        try
        {
            var httpClientFactory = _services.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("webhooks");

            // Add signature header for verification
            var payload = evt.PayloadJson ?? "{}";
            var signature = ComputeSignature(payload, evt.Project.WebhookSecret ?? "");

            var request = new HttpRequestMessage(HttpMethod.Post, destinationUrl);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            request.Headers.Add("X-LiveAuth-Signature", signature);
            request.Headers.Add("X-LiveAuth-Event", evt.EventType);
            request.Headers.Add("X-LiveAuth-Event-Id", evt.Id.ToString());

            var response = await httpClient.SendAsync(request, ct);
            evt.LastStatusCode = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                evt.Status = WebhookEventStatus.Delivered;
                evt.DeliveredAt = DateTime.UtcNow;
                evt.LastError = null;
                _logger.LogInformation("Webhook delivered successfully: {EventId} to {Url}", 
                    evt.Id, destinationUrl);
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                evt.Status = WebhookEventStatus.Failed;
                evt.LastError = $"HTTP {response.StatusCode}: {error[..Math.Min(500, error.Length)]}";
                evt.NextAttemptAt = DateTime.UtcNow.AddMinutes(GetRetryDelay(evt.AttemptCount));
                MarkDeadIfExhausted(evt);
                _logger.LogWarning("Webhook delivery failed: {EventId} - {Error}", 
                    evt.Id, evt.LastError);
            }
        }
        catch (Exception ex)
        {
            evt.Status = WebhookEventStatus.Failed;
            evt.LastError = ex.Message[..Math.Min(200, ex.Message.Length)];
            evt.NextAttemptAt = DateTime.UtcNow.AddMinutes(GetRetryDelay(evt.AttemptCount));
            MarkDeadIfExhausted(evt);
            _logger.LogError(ex, "Webhook delivery error: {EventId}", evt.Id);
        }

        await db.SaveChangesAsync(ct);
    }

    private static int GetRetryDelay(int attemptCount) => attemptCount switch
    {
        1 => 1,   // 1 minute
        2 => 5,   // 5 minutes  
        3 => 15,  // 15 minutes
        4 => 60,  // 1 hour
        _ => 60   // default 1 hour
    };

    private static void MarkDeadIfExhausted(WebhookEvent evt)
    {
        if (evt.AttemptCount < MaxAttempts) return;

        evt.Status = WebhookEventStatus.Dead;
        evt.NextAttemptAt = DateTime.MaxValue;
    }

    private static string ComputeSignature(string payload, string secret)
    {
        if (string.IsNullOrEmpty(secret))
            return "";
        
        using var hmac = new System.Security.Cryptography.HMACSHA256(
            System.Text.Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(hash);
    }
}

public static class WebhookDeliveryWorkerExtensions
{
    public static IServiceCollection AddWebhookDeliveryWorker(this IServiceCollection services)
    {
        services.AddHostedService<WebhookDeliveryWorker>();
        return services;
    }
}
