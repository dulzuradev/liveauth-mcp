using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Models;
using LiveAuthCore.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Controllers;

[ApiController]
[Route("api/admin/analytics")]
[Authorize(Roles = "Admin")]
public class AdminAnalyticsOverviewController : ControllerBase
{
    private readonly LiveAuthDbContext _db;
    private readonly BtcExchangeRateService _btcRate;

    public AdminAnalyticsOverviewController(LiveAuthDbContext db, BtcExchangeRateService btcRate)
    {
        _db = db;
        _btcRate = btcRate;
    }

    [HttpGet("overview")]
    public async Task<ActionResult<AdminAnalyticsOverviewResponse>> GetOverview(
        [FromQuery] int windowHours = 24,
        CancellationToken ct = default)
    {
        if (windowHours <= 0 || windowHours > 720)
            windowHours = 24;

        // 🔥 Assume CreatedAt is stored as LOCAL time
        var nowUtc = DateTime.UtcNow;
        var fromUtc = nowUtc.AddHours(-windowHours);

// IMPORTANT: normalize boundary kind only
        fromUtc = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);

        var authEvents = _db.AuthEvents
            .Where(e => e.CreatedAt >= fromUtc);

        // ---- Aggregates
        var authRequests = await authEvents.CountAsync(ct);
        var successes = await authEvents.CountAsync(e => e.Success, ct);
        var failures = await authEvents.CountAsync(e => !e.Success, ct);

        var rateLimits = await authEvents
            .CountAsync(e => e.EventType == AuthEventType.RateLimitHit, ct);

        var satsPaid = await authEvents
            .Where(e => e.SatsPaid.HasValue)
            .SumAsync(e => e.SatsPaid!.Value, ct);

        var paidAuths = await authEvents
            .CountAsync(e => e.SatsPaid > 0, ct);

        // === MCP Gate Metrics ===
        var mcpSessionsTotal = await _db.McpGateSessions.CountAsync(ct);
        var mcpSessionsActive = await _db.McpGateSessions
            .CountAsync(s => s.ExpiresAt > nowUtc, ct);
        var mcpTokensIssued = await _db.McpGateTokens.CountAsync(ct);
        // Real sats earned: sum SatsUsed from all issued tokens
        var mcpSatsEarned = await _db.McpGateTokens
            .SumAsync(t => t.SatsUsed, ct);

        // === L402 Metrics ===
        // L402 payments tracked via AuthEvents with specific event types
        var l402InvoicesCreated = await authEvents
            .CountAsync(e => e.Reason == "L402_INVOICE_CREATED", ct);
        var l402PaymentsReceived = await authEvents
            .CountAsync(e => e.Reason == "L402_PAYMENT_RECEIVED", ct);
        var l402SatsEarned = await authEvents
            .Where(e => e.Reason == "L402_PAYMENT_RECEIVED")
            .SumAsync(e => e.SatsPaid ?? 0, ct);

        // === Funnel Metrics ===
        var challengesIssued = await authEvents
            .CountAsync(e => e.EventType == AuthEventType.PowChallengeIssued, ct);
        var authsStarted = await authEvents
            .CountAsync(e => e.EventType == AuthEventType.LoginRequested, ct);
        var authsPaid = await authEvents
            .CountAsync(e => e.EventType == AuthEventType.LoginSucceeded && e.SatsPaid > 0, ct);
        var authsVerified = await authEvents
            .CountAsync(e => e.Success, ct);
        
        // Tokens used = successful verifications
        var tokensUsed = authsVerified;

        var funnel = new FunnelMetrics
        {
            ChallengesIssued = challengesIssued,
            AuthsStarted = authsStarted,
            AuthsPaid = authsPaid,
            AuthsVerified = authsVerified,
            TokensUsed = tokensUsed,
            StartToPaidRate = authsStarted > 0 ? (double)authsPaid / authsStarted * 100 : 0,
            PaidToVerifiedRate = authsPaid > 0 ? (double)authsVerified / authsPaid * 100 : 0,
            VerifiedToUsedRate = authsVerified > 0 ? (double)tokensUsed / authsVerified * 100 : 0
        };

        var totalProjects = await _db.Projects.CountAsync(ct);
        var activeProjects = await _db.Projects.CountAsync(p => p.IsActive, ct);
        var proProjects = await _db.Projects.CountAsync(p => p.Plan == "pro", ct);

        // ---- Time series (hourly)
        var authsOverTimeRaw = await authEvents
            .GroupBy(e => new
            {
                e.CreatedAt.Year,
                e.CreatedAt.Month,
                e.CreatedAt.Day,
                e.CreatedAt.Hour
            })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                g.Key.Day,
                g.Key.Hour,
                Successful = g.Count(e => e.Success),
                Failed = g.Count(e => !e.Success)
            })
            .OrderBy(x => x.Year)
            .ThenBy(x => x.Month)
            .ThenBy(x => x.Day)
            .ThenBy(x => x.Hour)
            .ToListAsync(ct);

        var authsOverTime = authsOverTimeRaw
            .Select(x => new AuthsOverTimePoint
            {
                // Convert to UTC *after* querying
                TimestampUtc = new DateTime(
                    x.Year, x.Month, x.Day, x.Hour, 0, 0, DateTimeKind.Local
                ).ToUniversalTime(),
                Successful = x.Successful,
                Failed = x.Failed
            })
            .ToList();

        // ---- Recent events
        var rawEvents = await authEvents
            .AsNoTracking()
            .OrderByDescending(e => e.CreatedAt)
            .Take(100)
            .Select(e => new
            {
                e.CreatedAt,
                e.ProjectId,
                ProjectName = e.Project != null ? e.Project.Name : null,
                e.EventType,
                e.Success,
                e.SatsPaid,
                e.Reason,
                e.ClientIp
            })
            .ToListAsync(ct);

        var recentEvents = rawEvents
            .Select(e => new AdminAuthEventDto
            {
                Timestamp = DateTime.SpecifyKind(e.CreatedAt, DateTimeKind.Local)
                    .ToUniversalTime(),
                ProjectId = e.ProjectId,
                ProjectName = e.ProjectName ?? "(deleted project)",
                EventType = e.EventType.ToString(),
                Success = e.Success,
                SatsPaid = e.SatsPaid,
                Reason = e.Reason,
                ClientIpMasked = MaskIp(e.ClientIp)
            })
            .ToList();

        var totalSatsEarned = satsPaid + mcpSatsEarned + l402SatsEarned;

        // Fetch exchange rate once for all USD conversions
        var btcUsdRate = await _btcRate.GetBtcUsdRateAsync(ct);
        double? totalSatsEarnedUsd = btcUsdRate.HasValue
            ? totalSatsEarned / 100_000_000.0 * btcUsdRate.Value
            : null;
        double? mcpSatsEarnedUsd = btcUsdRate.HasValue && mcpSatsEarned > 0
            ? mcpSatsEarned / 100_000_000.0 * btcUsdRate.Value
            : null;
        double? l402SatsEarnedUsd = btcUsdRate.HasValue && l402SatsEarned > 0
            ? l402SatsEarned / 100_000_000.0 * btcUsdRate.Value
            : null;

        return Ok(new AdminAnalyticsOverviewResponse
        {
            TotalProjects = totalProjects,
            ActiveProjects = activeProjects,

            AuthRequests = authRequests,
            AuthSuccesses = successes,
            AuthFailures = failures,
            RateLimitHits = rateLimits,

            SatsPaid = satsPaid,
            PaidAuths = paidAuths,

            ProProjects = proProjects,
            FreeProjects = totalProjects - proProjects,

            // MCP Metrics
            McpSessionsTotal = mcpSessionsTotal,
            McpSessionsActive = mcpSessionsActive,
            McpTokensIssued = mcpTokensIssued,
            McpSatsEarned = mcpSatsEarned,
            McpSatsEarnedUsd = mcpSatsEarnedUsd,

            // L402 Metrics
            L402InvoicesCreated = l402InvoicesCreated,
            L402PaymentsReceived = l402PaymentsReceived,
            L402SatsEarned = l402SatsEarned,
            L402SatsEarnedUsd = l402SatsEarnedUsd,

            // Exchange Rate
            BtcUsdRate = btcUsdRate,
            TotalSatsEarnedUsd = totalSatsEarnedUsd,

            // Funnel
            Funnel = funnel,

            // Expose UTC window externally
            WindowStart = fromUtc.ToUniversalTime(),
            WindowEnd = nowUtc.ToUniversalTime(),

            AuthsOverTime = authsOverTime,
            RecentEvents = recentEvents
        });
    }

    private static string MaskIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return "";
        var parts = ip.Split('.');
        return parts.Length == 4 ? $"{parts[0]}.{parts[1]}.x.x" : ip;
    }
}