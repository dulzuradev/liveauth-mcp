namespace LiveAuthCore.Services;

using System.Text.Json;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using Microsoft.EntityFrameworkCore;

public class WebhookService
{
    private readonly LiveAuthDbContext _db;

    public WebhookService(LiveAuthDbContext db)
    {
        _db = db;
    }

    public async Task EnqueueAsync(Project project, string eventType, object payload, CancellationToken ct = default)
        => await EnqueueAsync(project, eventType, payload, destinationUrl: null, ct);

    /// <summary>
    /// Enqueue a webhook event for the given project.
    /// If no destination or project webhook URL is configured, this is a no-op.
    /// </summary>
    public async Task EnqueueAsync(
        Project project,
        string eventType,
        object payload,
        string? destinationUrl,
        CancellationToken ct = default)
    {
        var targetUrl = (string.IsNullOrWhiteSpace(destinationUrl)
            ? project.WebhookUrl
            : destinationUrl)?.Trim();

        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            // No webhook configured – nothing to do
            return;
        }

        var json = JsonSerializer.Serialize(
            payload,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var now = DateTime.UtcNow;

        var evt = new WebhookEvent
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            EventType = eventType,
            PayloadJson = json,
            DestinationUrl = targetUrl,
            CreatedAt = now,
            NextAttemptAt = now,
            AttemptCount = 0,
            Status = WebhookEventStatus.Pending
        };

        _db.WebhookEvents.Add(evt);
        await _db.SaveChangesAsync(ct);
    }
}
