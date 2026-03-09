namespace LiveAuthCore.Models;

public record ApiLogEntry
{
    public DateTime Timestamp { get; init; }
    public string TimestampLocal => Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string IpMasked { get; init; } = "";
    public long Sats { get; init; }
    public string Status { get; init; } = "";
    public string Reason { get; init; } = "";
}
