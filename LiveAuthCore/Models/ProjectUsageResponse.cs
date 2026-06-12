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
    
    public long L402BalanceSats { get; set; }

    public int McpSatsPerCall { get; set; }
    public int McpInvoiceCallCredits { get; set; }
    public long McpMaxSatsPerDay { get; set; }
    public int McpMaxCallsPerMinute { get; set; }
    public int McpSessionsTotal { get; set; }
    public int McpSessionsActive { get; set; }
    public int McpTokensIssued { get; set; }
    public int McpTokensActive { get; set; }
    public long McpCallsUsed { get; set; }
    public long McpSatsUsed { get; set; }
    public long McpActiveBudgetSats { get; set; }
    public long McpPaidToolCalls { get; set; }
    public long McpPaidToolSatsCharged { get; set; }
    public long McpPaidToolNetSats { get; set; }
    public long McpDeniedToolCharges { get; set; }
}
