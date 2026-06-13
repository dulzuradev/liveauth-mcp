namespace LiveAuthCore.Models;

public sealed class AdminCommandCenterResponse
{
    public int WindowHours { get; set; }
    public DateTime WindowStart { get; set; }
    public DateTime WindowEnd { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public double? BtcUsdRate { get; set; }

    public AdminCommandCenterRevenue Revenue { get; set; } = new();
    public AdminCommandCenterAuth Auth { get; set; } = new();
    public AdminCommandCenterMcp Mcp { get; set; } = new();
    public AdminCommandCenterL402 L402 { get; set; } = new();
    public AdminCommandCenterWebhooks Webhooks { get; set; } = new();
    public LightningFeeSettingsResponse Fees { get; set; } = new(200, 1, 1500, 1, 500, 1, null);

    public List<AdminCommandCenterAlert> Attention { get; set; } = [];
    public List<AdminCommandCenterMcpTool> TopMcpTools { get; set; } = [];
    public List<AdminCommandCenterWebhookItem> WebhookFailures { get; set; } = [];
    public List<AdminAuthEventDto> RecentAuthEvents { get; set; } = [];
}

public sealed class AdminCommandCenterRevenue
{
    public long TotalSats { get; set; }
    public double? TotalUsd { get; set; }
    public double? ProjectedMonthlyUsd { get; set; }
    public double? TargetMinProgressPercent { get; set; }
    public double? TargetMaxProgressPercent { get; set; }
    public int TargetMinMonthlyUsd { get; set; } = 10_000;
    public int TargetMaxMonthlyUsd { get; set; } = 20_000;

    public long LightningAuthGrossSats { get; set; }
    public long LightningAuthFeeSats { get; set; }
    public long L402InvoiceGrossSats { get; set; }
    public long L402InvoiceFeeSats { get; set; }
    public long L402BundleGrossSats { get; set; }
    public long L402BundleMarkupSats { get; set; }
    public long McpPaidToolGrossSats { get; set; }
    public long McpPaidToolPlatformFeeSats { get; set; }
    public long McpPaidToolNetSats { get; set; }
}

public sealed class AdminCommandCenterAuth
{
    public int TotalProjects { get; set; }
    public int ActiveProjects { get; set; }
    public int ProProjects { get; set; }
    public int FreeProjects { get; set; }
    public int ActiveAuthSessions { get; set; }
    public int PendingInvoices { get; set; }

    public int AuthRequests { get; set; }
    public int AuthSuccesses { get; set; }
    public int AuthFailures { get; set; }
    public int PaidAuths { get; set; }
    public int RateLimitHits { get; set; }
    public double SuccessRate { get; set; }
    public double FailureRate { get; set; }
    public double RateLimitRate { get; set; }

    public FunnelMetrics Funnel { get; set; } = new();
    public List<AuthsOverTimePoint> AuthsOverTime { get; set; } = [];
}

public sealed class AdminCommandCenterMcp
{
    public int SessionsTotal { get; set; }
    public int SessionsActive { get; set; }
    public int TokensIssued { get; set; }
    public int TokensActive { get; set; }
    public long CallsUsed { get; set; }
    public long SatsUsed { get; set; }
    public long PaidToolCalls { get; set; }
    public long PaidToolGrossSats { get; set; }
    public long PaidToolPlatformFeeSats { get; set; }
    public long PaidToolNetSats { get; set; }
    public long DeniedCharges { get; set; }
    public long InactiveToolDenials { get; set; }
    public int ActiveTools { get; set; }
    public int NonActiveTools { get; set; }
}

public sealed class AdminCommandCenterL402
{
    public int PurchasesPending { get; set; }
    public int PurchasesSettling { get; set; }
    public int PurchasesSettled { get; set; }
    public int PurchasesExpired { get; set; }
    public long PurchaseTotalChargedSats { get; set; }
    public long PurchaseInvoiceFeeSats { get; set; }

    public int BundlesPending { get; set; }
    public int BundlesActive { get; set; }
    public int BundlesExpired { get; set; }
    public int BundlesDepleted { get; set; }
    public long BundleTotalChargedSats { get; set; }
    public long BundleMarkupSats { get; set; }
    public int BundleCallsRemaining { get; set; }

    public int MacaroonsIssued { get; set; }
    public int MacaroonsActive { get; set; }
    public int MacaroonsRevoked { get; set; }
}

public sealed class AdminCommandCenterWebhooks
{
    public int Pending { get; set; }
    public int InProgress { get; set; }
    public int Delivered { get; set; }
    public int Failed { get; set; }
    public int Dead { get; set; }
    public int DueNow { get; set; }
    public DateTime? OldestPendingAt { get; set; }
    public DateTime? OldestNextAttemptAt { get; set; }
}

public sealed class AdminCommandCenterAlert
{
    public string Severity { get; set; } = "info";
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public long Count { get; set; }
}

public sealed class AdminCommandCenterMcpTool
{
    public Guid ToolId { get; set; }
    public string ToolName { get; set; } = "";
    public string ToolSlug { get; set; } = "";
    public string ToolStatus { get; set; } = "";
    public long Calls { get; set; }
    public long GrossSats { get; set; }
    public long PlatformFeeSats { get; set; }
    public long NetSats { get; set; }
    public long DeniedCharges { get; set; }
    public double AverageGrossSatsPerCall { get; set; }
}

public sealed class AdminCommandCenterWebhookItem
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string ProjectName { get; set; } = "";
    public string EventType { get; set; } = "";
    public string Status { get; set; } = "";
    public int AttemptCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public int? LastStatusCode { get; set; }
    public string? LastError { get; set; }
}
