namespace LiveAuthCore.Models;

public sealed class AdminProjectUsageDto
{
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = "";
    public string Plan { get; set; } = "";

    public int Auths { get; set; }
    public int Successes { get; set; }
    public int Failures { get; set; }
    public int RateLimitHits { get; set; }

    public long SatsPaid { get; set; }
    public DateTime? ProPaidUntil { get; set; }
}