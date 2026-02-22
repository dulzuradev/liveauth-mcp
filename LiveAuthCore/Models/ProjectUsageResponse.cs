namespace LiveAuthCore.Models;

public sealed class ProjectUsageResponse
{
    public string Plan { get; set; } = "free";
    public bool IsPro { get; set; }
    public DateTime? ProExpiresAt { get; set; }
    
    public int MonthlyLimit { get; set; }
    public int MonthlyUsed { get; set; }
    public int MonthlyRemaining { get; set; }
    public double MonthlyUsagePercent { get; set; }
    
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    
    public long TotalSatsCharged { get; set; }
    public int TotalVerifications { get; set; }
}
