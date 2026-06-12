using LiveAuthCore.Data.Entities;

namespace LiveAuthCore.Models;

public sealed class WebhookEventDto
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public int AttemptCount { get; set; }
    public string Status { get; set; } = default!;
    public int? LastStatusCode { get; set; }
    public string? LastError { get; set; }
    public string? DestinationUrl { get; set; }
}

public sealed class ListWebhookEventsResponse
{
    public IReadOnlyList<WebhookEventDto> Events { get; set; } = Array.Empty<WebhookEventDto>();
}
