namespace LiveAuthCore.Models;

public sealed class AdminAnalyticsOverviewResponse
{
    // existing
    public int TotalProjects { get; set; }
    public int ActiveProjects { get; set; }

    public int AuthRequests { get; set; }
    public int AuthSuccesses { get; set; }
    public int AuthFailures { get; set; }
    public int RateLimitHits { get; set; }

    public long SatsPaid { get; set; }
    public int PaidAuths { get; set; }

    public int ProProjects { get; set; }
    public int ProExpired { get; set; }

    // 🔥 NEW (high value)
    public int FreeProjects { get; set; }
    public int ProjectsInGracePeriod { get; set; }

    public int ActiveAuthSessions { get; set; }   // unpaid + unexpired
    public int PendingInvoices { get; set; }      // auth + subscription

    // === MCP Usage ===
    public int McpSessionsTotal { get; set; }
    public int McpSessionsActive { get; set; }
    public int McpTokensIssued { get; set; }
    public long McpSatsEarned { get; set; }

    // === L402 Usage ===
    public int L402InvoicesCreated { get; set; }
    public int L402PaymentsReceived { get; set; }
    public long L402SatsEarned { get; set; }

    // === Funnel Analytics ===
    public FunnelMetrics Funnel { get; set; } = new();

    public DateTime WindowStart { get; set; }
    public DateTime WindowEnd { get; set; }
    
    public List<AuthsOverTimePoint> AuthsOverTime { get; set; } = new();
    
    public List<AdminAuthEventDto> RecentEvents { get; set; } = [];
}

public sealed class FunnelMetrics
{
    public int ChallengesIssued { get; set; }
    public int AuthsStarted { get; set; }
    public int AuthsPaid { get; set; }
    public int AuthsVerified { get; set; }
    public int TokensUsed { get; set; }
    
    public double StartToPaidRate { get; set; }
    public double PaidToVerifiedRate { get; set; }
    public double VerifiedToUsedRate { get; set; }
}

public sealed class AuthsOverTimePoint
{
    public DateTime TimestampUtc { get; set; }
    public long Successful { get; set; }
    public long Failed { get; set; }
}



