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

    public DateTime WindowStart { get; set; }
    public DateTime WindowEnd { get; set; }
    
    public List<AuthsOverTimePoint> AuthsOverTime { get; set; } = new();
    
    public List<AdminAuthEventDto> RecentEvents { get; set; } = [];
}

public sealed class AuthsOverTimePoint
{
    public DateTime TimestampUtc { get; set; }
    public long Successful { get; set; }
    public long Failed { get; set; }
}



