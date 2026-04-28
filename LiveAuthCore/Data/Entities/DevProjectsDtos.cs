using Google.Protobuf.WellKnownTypes;

namespace LiveAuthCore.Data.Entities;

public sealed class ProjectSettingsResponse
{
    public List<string> AllowedDomains { get; set; } = new();
    public string? WebhookUrl { get; set; }
    public int SatsPerLogin { get; set; }
    public int MaxAuthsPerIpPerHour { get; set; }
    
    public bool AllowDemoAuth { get; set; }

    // Custom LND node config
    public bool? UseCustomNode { get; set; }
    public string? LndBaseUrl { get; set; }
    public string? LndMacaroon { get; set; }
}


public sealed class UpdateProjectSettingsRequest
{
    public List<string> AllowedDomains { get; set; } = new();
    public string? WebhookUrl { get; set; }
    public int SatsPerLogin { get; set; }
    public int MaxAuthsPerIpPerHour { get; set; }
    
    public bool AllowDemoAuth { get; set; }

    // Custom LND node config
    public bool UseCustomNode { get; set; }
    public string? LndBaseUrl { get; set; }
    public string? LndMacaroon { get; set; }
}

public sealed class TestLndConnectionRequest
{
    public string BaseUrl { get; set; } = string.Empty;
    public string? Macaroon { get; set; }
}

public sealed class TestLndConnectionResponse
{
    public bool Success { get; set; }
    public string? Version { get; set; }
    public long BlockHeight { get; set; }
    public int NumActiveChannels { get; set; }
    public int NumPeers { get; set; }
    public string? Error { get; set; }
}

public sealed class UpdateProjectStatusRequest
{
    public bool Active { get; set; }
}

public sealed class AnalyticsSummary
{
    public int TotalAuths24h { get; set; }
    public int Success24h { get; set; }
    public int Failed24h { get; set; }
    public long SatsPaid24h { get; set; }
    
    public int RateLimitHits24h { get; set; }
}

public sealed class LogEntry
{
    public required DateTime Timestamp { get; set; }
    public required string IpMasked { get; set; } = string.Empty;
    public long Sats { get; set; }
    public required string Status { get; set; } = string.Empty;
    public required string Reason { get; set; } = string.Empty;
}
