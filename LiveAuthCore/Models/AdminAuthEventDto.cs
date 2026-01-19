namespace LiveAuthCore.Models;

public sealed class AdminAuthEventDto
{
    public DateTime Timestamp { get; set; }
    public Guid ProjectId { get; set; }
    public string ProjectName { get; set; } = "";

    public string EventType { get; set; } = "";
    public bool Success { get; set; }

    public long? SatsPaid { get; set; }
    public string? Reason { get; set; }
    public string ClientIpMasked { get; set; } = "";
}