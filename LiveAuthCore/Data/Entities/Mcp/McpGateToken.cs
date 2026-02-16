using System.ComponentModel.DataAnnotations;

namespace LiveAuthCore.Data.Entities.Mcp;

public class McpGateToken
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }

    public Guid SessionId { get; set; }

    public string JwtId { get; set; } = Guid.NewGuid().ToString("N");

    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(10);

    // Metering
    public long CallsUsed { get; set; }
    public long SatsUsed { get; set; }

    public long MaxCallsPerMinute { get; set; } = 60;
    public long MaxSatsPerDay { get; set; } = 10_000;

    public DateTime DayWindowStart { get; set; } = DateTime.UtcNow.Date;

    public string Status { get; set; } = "active"; // active|revoked|expired

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
