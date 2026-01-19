namespace LiveAuthCore.Data.Entities;

public class UsageEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public string Type { get; set; } = string.Empty; // "invoice_created", "verified"
    public long SatsCharged { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}