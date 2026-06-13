using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Models;
using LiveAuthCore.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Controllers;

[ApiController]
[Route("api/admin/analytics/command-center")]
[Authorize(Roles = "Admin")]
public class AdminCommandCenterController : ControllerBase
{
    private const int TargetMinMonthlyUsd = 10_000;
    private const int TargetMaxMonthlyUsd = 20_000;

    private readonly LiveAuthDbContext _db;
    private readonly BtcExchangeRateService _btcRate;
    private readonly LightningFeeSettingsService _feeSettings;

    public AdminCommandCenterController(
        LiveAuthDbContext db,
        BtcExchangeRateService btcRate,
        LightningFeeSettingsService feeSettings)
    {
        _db = db;
        _btcRate = btcRate;
        _feeSettings = feeSettings;
    }

    [HttpGet]
    public async Task<ActionResult<AdminCommandCenterResponse>> Get(
        [FromQuery] int windowHours = 24,
        CancellationToken ct = default)
    {
        windowHours = Math.Clamp(windowHours, 1, 720);

        var nowUtc = DateTime.UtcNow;
        var fromUtc = DateTime.SpecifyKind(nowUtc.AddHours(-windowHours), DateTimeKind.Utc);
        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var authEvents = _db.AuthEvents.Where(e => e.CreatedAt >= fromUtc);
        var authRequests = await authEvents.CountAsync(ct);
        var authSuccesses = await authEvents.CountAsync(e => e.Success, ct);
        var authFailures = await authEvents.CountAsync(e => !e.Success, ct);
        var paidAuths = await authEvents.CountAsync(e => e.SatsPaid > 0, ct);
        var rateLimitHits = await authEvents.CountAsync(e => e.EventType == AuthEventType.RateLimitHit, ct);

        var authSessionsWindow = _db.AuthSessions.Where(s => (s.PaidAt ?? s.CreatedAt) >= fromUtc);
        var lightningAuthGrossSats = await authSessionsWindow
            .Where(s => s.IsPaid)
            .SumAsync(s => (long?)(s.TotalChargedSats > 0 ? s.TotalChargedSats : s.AmountSats), ct) ?? 0L;
        var lightningAuthFeeSats = await authSessionsWindow
            .Where(s => s.IsPaid)
            .SumAsync(s => (long?)s.InvoiceFeeSats, ct) ?? 0L;

        var activeAuthSessions = await _db.AuthSessions
            .CountAsync(s => !s.IsPaid && s.ExpiresAt > nowUtc, ct);
        var authPendingInvoices = await _db.AuthSessions
            .CountAsync(s => !s.IsPaid && s.InvoiceBolt11 != null && s.ExpiresAt > nowUtc, ct);
        var subscriptionPendingInvoices = await _db.BillingSubscriptions
            .CountAsync(s => !s.IsPaid && s.ExpiresAt > nowUtc, ct);

        var totalProjects = await _db.Projects.CountAsync(p => !p.IsDeleted, ct);
        var activeProjects = await _db.Projects.CountAsync(p => !p.IsDeleted && p.IsActive, ct);
        var proProjects = await _db.Projects.CountAsync(p => !p.IsDeleted && p.Plan == "pro", ct);

        var mcpSessionsTotal = await _db.McpGateSessions.CountAsync(ct);
        var mcpSessionsActive = await _db.McpGateSessions.CountAsync(s => s.ExpiresAt > nowUtc, ct);
        var mcpPendingInvoices = await _db.McpGateSessions.CountAsync(s =>
            s.LightningInvoice != null &&
            s.Status == "pending" &&
            s.ExpiresAt > nowUtc, ct);
        var mcpTokensIssued = await _db.McpGateTokens.CountAsync(ct);
        var mcpTokensActive = await _db.McpGateTokens.CountAsync(t =>
            t.Status == "active" &&
            t.ExpiresAt > nowUtc, ct);
        var mcpCallsUsed = await _db.McpGateTokens.SumAsync(t => (long?)t.CallsUsed, ct) ?? 0L;
        var mcpSatsUsed = await _db.McpGateTokens.SumAsync(t => (long?)t.SatsUsed, ct) ?? 0L;

        var mcpTools = await _db.McpTools
            .AsNoTracking()
            .Where(t => t.RemovedAt == null)
            .Select(t => new CommandCenterMcpToolSeed(t.Id, t.Name, t.Slug, t.Status))
            .ToListAsync(ct);
        var activeTools = mcpTools.Count(t => string.Equals(t.Status, "Active", StringComparison.OrdinalIgnoreCase));
        var nonActiveTools = mcpTools.Count - activeTools;

        var mcpToolEvents = _db.McpToolRevenueEvents.Where(e => e.CreatedAt >= fromUtc);
        var mcpPaidToolCalls = await mcpToolEvents.LongCountAsync(e => e.Status == "Charged", ct);
        var mcpPaidToolGrossSats = await mcpToolEvents
            .Where(e => e.Status == "Charged")
            .SumAsync(e => (long?)e.GrossSats, ct) ?? 0L;
        var mcpPaidToolPlatformFeeSats = await mcpToolEvents
            .Where(e => e.Status == "Charged")
            .SumAsync(e => (long?)e.PlatformFeeSats, ct) ?? 0L;
        var mcpPaidToolNetSats = await mcpToolEvents
            .Where(e => e.Status == "Charged")
            .SumAsync(e => (long?)e.NetSats, ct) ?? 0L;
        var mcpDeniedCharges = await mcpToolEvents.LongCountAsync(e => e.Status == "Denied", ct);
        var inactiveToolDenials = await mcpToolEvents.LongCountAsync(e =>
            e.Status == "Denied" &&
            e.MetadataJson != null &&
            EF.Functions.Like(e.MetadataJson, "%tool_inactive%"), ct);

        var topMcpTools = await GetTopMcpToolsAsync(mcpTools, fromUtc, ct);

        var l402PurchaseWindow = _db.L402Purchases.Where(p => (p.SettledAt ?? p.CreatedAt) >= fromUtc);
        var l402PurchaseTotalChargedSats = await l402PurchaseWindow
            .Where(p => p.Status == "settled" || p.Status == "settling")
            .SumAsync(p => (long?)p.TotalChargedSats, ct) ?? 0L;
        var l402PurchaseInvoiceFeeSats = await l402PurchaseWindow
            .Where(p => p.Status == "settled" || p.Status == "settling")
            .SumAsync(p => (long?)p.InvoiceFeeSats, ct) ?? 0L;

        var l402PurchasesPending = await _db.L402Purchases.CountAsync(p => p.Status == "pending", ct);
        var l402PurchasesSettling = await _db.L402Purchases.CountAsync(p => p.Status == "settling", ct);
        var l402PurchasesSettled = await _db.L402Purchases.CountAsync(p => p.Status == "settled", ct);
        var l402PurchasesExpired = await _db.L402Purchases.CountAsync(p => p.Status == "expired", ct);
        var l402PendingInvoices = await _db.L402Purchases.CountAsync(p =>
            p.Status == "pending" &&
            p.ExpiresAtUnix > nowUnix, ct);

        var l402BundleWindow = _db.L402Bundles.Where(b => b.CreatedAt >= fromUtc);
        var l402BundleTotalChargedSats = await l402BundleWindow
            .Where(b => b.Status == "paid" || b.Status == "active" || b.Status == "depleted")
            .SumAsync(b => (long?)b.TotalChargedSats, ct) ?? 0L;
        var l402BundleMarkupSats = await l402BundleWindow
            .Where(b => b.Status == "paid" || b.Status == "active" || b.Status == "depleted")
            .SumAsync(b => (long?)b.MarkupSats, ct) ?? 0L;

        var l402BundlesPending = await _db.L402Bundles.CountAsync(b => b.Status == "pending", ct);
        var l402BundlesActive = await _db.L402Bundles.CountAsync(b =>
            b.Status == "active" &&
            b.ExpiresAtUnix > nowUnix &&
            b.RemainingCalls > 0, ct);
        var l402BundlesExpired = await _db.L402Bundles.CountAsync(b =>
            b.Status == "expired" ||
            b.ExpiresAtUnix <= nowUnix, ct);
        var l402BundlesDepleted = await _db.L402Bundles.CountAsync(b =>
            b.Status == "depleted" ||
            b.RemainingCalls <= 0, ct);
        var l402BundleCallsRemaining = await _db.L402Bundles
            .Where(b => b.Status == "active" && b.ExpiresAtUnix > nowUnix)
            .SumAsync(b => (int?)b.RemainingCalls, ct) ?? 0;

        var macaroonsIssued = await _db.L402Macaroons.CountAsync(ct);
        var macaroonsActive = await _db.L402Macaroons.CountAsync(m =>
            !m.IsRevoked &&
            m.ExpiresAtUnix > nowUnix, ct);
        var macaroonsRevoked = await _db.L402Macaroons.CountAsync(m => m.IsRevoked, ct);

        var webhookCounts = await GetWebhookCountsAsync(nowUtc, ct);
        var webhookFailures = await GetWebhookFailuresAsync(ct);

        var authsOverTime = await GetAuthsOverTimeAsync(authEvents, ct);
        var recentAuthEvents = await GetRecentAuthEventsAsync(authEvents, ct);
        var funnel = new FunnelMetrics
        {
            ChallengesIssued = await authEvents.CountAsync(e => e.EventType == AuthEventType.PowChallengeIssued, ct),
            AuthsStarted = await authEvents.CountAsync(e => e.EventType == AuthEventType.LoginRequested, ct),
            AuthsPaid = await authEvents.CountAsync(e => e.EventType == AuthEventType.LoginSucceeded && e.SatsPaid > 0, ct),
            AuthsVerified = authSuccesses,
            TokensUsed = authSuccesses
        };
        funnel.StartToPaidRate = Percent(funnel.AuthsPaid, funnel.AuthsStarted);
        funnel.PaidToVerifiedRate = Percent(funnel.AuthsVerified, funnel.AuthsPaid);
        funnel.VerifiedToUsedRate = Percent(funnel.TokensUsed, funnel.AuthsVerified);

        var feeResponse = await _feeSettings.GetResponseAsync(ct);
        var btcUsdRate = await _btcRate.GetBtcUsdRateAsync(ct);
        var totalFeeRevenueSats =
            lightningAuthFeeSats +
            l402PurchaseInvoiceFeeSats +
            l402BundleMarkupSats +
            mcpPaidToolPlatformFeeSats;
        var totalUsd = ToUsd(totalFeeRevenueSats, btcUsdRate);
        double? projectedMonthlyUsd = totalUsd.HasValue
            ? totalUsd.Value / windowHours * 24 * 30
            : null;

        var auth = new AdminCommandCenterAuth
        {
            TotalProjects = totalProjects,
            ActiveProjects = activeProjects,
            ProProjects = proProjects,
            FreeProjects = totalProjects - proProjects,
            ActiveAuthSessions = activeAuthSessions,
            PendingInvoices = authPendingInvoices + subscriptionPendingInvoices + mcpPendingInvoices + l402PendingInvoices,
            AuthRequests = authRequests,
            AuthSuccesses = authSuccesses,
            AuthFailures = authFailures,
            PaidAuths = paidAuths,
            RateLimitHits = rateLimitHits,
            SuccessRate = Percent(authSuccesses, authRequests),
            FailureRate = Percent(authFailures, authRequests),
            RateLimitRate = Percent(rateLimitHits, authRequests),
            Funnel = funnel,
            AuthsOverTime = authsOverTime
        };

        var mcp = new AdminCommandCenterMcp
        {
            SessionsTotal = mcpSessionsTotal,
            SessionsActive = mcpSessionsActive,
            TokensIssued = mcpTokensIssued,
            TokensActive = mcpTokensActive,
            CallsUsed = mcpCallsUsed,
            SatsUsed = mcpSatsUsed,
            PaidToolCalls = mcpPaidToolCalls,
            PaidToolGrossSats = mcpPaidToolGrossSats,
            PaidToolPlatformFeeSats = mcpPaidToolPlatformFeeSats,
            PaidToolNetSats = mcpPaidToolNetSats,
            DeniedCharges = mcpDeniedCharges,
            InactiveToolDenials = inactiveToolDenials,
            ActiveTools = activeTools,
            NonActiveTools = nonActiveTools
        };

        var l402 = new AdminCommandCenterL402
        {
            PurchasesPending = l402PurchasesPending,
            PurchasesSettling = l402PurchasesSettling,
            PurchasesSettled = l402PurchasesSettled,
            PurchasesExpired = l402PurchasesExpired,
            PurchaseTotalChargedSats = l402PurchaseTotalChargedSats,
            PurchaseInvoiceFeeSats = l402PurchaseInvoiceFeeSats,
            BundlesPending = l402BundlesPending,
            BundlesActive = l402BundlesActive,
            BundlesExpired = l402BundlesExpired,
            BundlesDepleted = l402BundlesDepleted,
            BundleTotalChargedSats = l402BundleTotalChargedSats,
            BundleMarkupSats = l402BundleMarkupSats,
            BundleCallsRemaining = l402BundleCallsRemaining,
            MacaroonsIssued = macaroonsIssued,
            MacaroonsActive = macaroonsActive,
            MacaroonsRevoked = macaroonsRevoked
        };

        var revenue = new AdminCommandCenterRevenue
        {
            TotalSats = totalFeeRevenueSats,
            TotalUsd = totalUsd,
            ProjectedMonthlyUsd = projectedMonthlyUsd,
            TargetMinProgressPercent = projectedMonthlyUsd.HasValue
                ? Math.Round(projectedMonthlyUsd.Value / TargetMinMonthlyUsd * 100, 1)
                : null,
            TargetMaxProgressPercent = projectedMonthlyUsd.HasValue
                ? Math.Round(projectedMonthlyUsd.Value / TargetMaxMonthlyUsd * 100, 1)
                : null,
            LightningAuthGrossSats = lightningAuthGrossSats,
            LightningAuthFeeSats = lightningAuthFeeSats,
            L402InvoiceGrossSats = l402PurchaseTotalChargedSats,
            L402InvoiceFeeSats = l402PurchaseInvoiceFeeSats,
            L402BundleGrossSats = l402BundleTotalChargedSats,
            L402BundleMarkupSats = l402BundleMarkupSats,
            McpPaidToolGrossSats = mcpPaidToolGrossSats,
            McpPaidToolPlatformFeeSats = mcpPaidToolPlatformFeeSats,
            McpPaidToolNetSats = mcpPaidToolNetSats
        };

        return Ok(new AdminCommandCenterResponse
        {
            WindowHours = windowHours,
            WindowStart = fromUtc.ToUniversalTime(),
            WindowEnd = nowUtc.ToUniversalTime(),
            GeneratedAtUtc = nowUtc.ToUniversalTime(),
            BtcUsdRate = btcUsdRate,
            Revenue = revenue,
            Auth = auth,
            Mcp = mcp,
            L402 = l402,
            Webhooks = webhookCounts,
            Fees = feeResponse,
            Attention = BuildAttention(auth, mcp, l402, webhookCounts),
            TopMcpTools = topMcpTools,
            WebhookFailures = webhookFailures,
            RecentAuthEvents = recentAuthEvents
        });
    }

    private async Task<List<AdminCommandCenterMcpTool>> GetTopMcpToolsAsync(
        IReadOnlyCollection<CommandCenterMcpToolSeed> tools,
        DateTime fromUtc,
        CancellationToken ct)
    {
        if (tools.Count == 0)
            return [];

        var toolIds = tools.Select(t => t.Id).ToList();
        var topRaw = await _db.McpToolRevenueEvents
            .Where(e => toolIds.Contains(e.McpToolId) && e.CreatedAt >= fromUtc)
            .GroupBy(e => e.McpToolId)
            .Select(g => new
            {
                ToolId = g.Key,
                Calls = g.LongCount(e => e.Status == "Charged"),
                GrossSats = g.Where(e => e.Status == "Charged").Sum(e => (long?)e.GrossSats) ?? 0L,
                PlatformFeeSats = g.Where(e => e.Status == "Charged").Sum(e => (long?)e.PlatformFeeSats) ?? 0L,
                NetSats = g.Where(e => e.Status == "Charged").Sum(e => (long?)e.NetSats) ?? 0L,
                DeniedCharges = g.LongCount(e => e.Status == "Denied")
            })
            .OrderByDescending(t => t.GrossSats)
            .ThenByDescending(t => t.Calls)
            .Take(8)
            .ToListAsync(ct);

        var toolMap = tools.ToDictionary(t => t.Id);
        return topRaw
            .Where(t => toolMap.ContainsKey(t.ToolId))
            .Select(t =>
            {
                var tool = toolMap[t.ToolId];
                return new AdminCommandCenterMcpTool
                {
                    ToolId = t.ToolId,
                    ToolName = tool.Name,
                    ToolSlug = tool.Slug,
                    ToolStatus = tool.Status,
                    Calls = t.Calls,
                    GrossSats = t.GrossSats,
                    PlatformFeeSats = t.PlatformFeeSats,
                    NetSats = t.NetSats,
                    DeniedCharges = t.DeniedCharges,
                    AverageGrossSatsPerCall = t.Calls > 0
                        ? Math.Round((double)t.GrossSats / t.Calls, 2)
                        : 0
                };
            })
            .ToList();
    }

    private async Task<AdminCommandCenterWebhooks> GetWebhookCountsAsync(DateTime nowUtc, CancellationToken ct)
    {
        var oldestPendingAt = await _db.WebhookEvents
            .Where(e => e.Status == WebhookEventStatus.Pending || e.Status == WebhookEventStatus.Failed)
            .OrderBy(e => e.CreatedAt)
            .Select(e => (DateTime?)e.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var oldestNextAttemptAt = await _db.WebhookEvents
            .Where(e =>
                (e.Status == WebhookEventStatus.Pending || e.Status == WebhookEventStatus.Failed) &&
                e.NextAttemptAt <= nowUtc)
            .OrderBy(e => e.NextAttemptAt)
            .Select(e => (DateTime?)e.NextAttemptAt)
            .FirstOrDefaultAsync(ct);

        return new AdminCommandCenterWebhooks
        {
            Pending = await _db.WebhookEvents.CountAsync(e => e.Status == WebhookEventStatus.Pending, ct),
            InProgress = await _db.WebhookEvents.CountAsync(e => e.Status == WebhookEventStatus.InProgress, ct),
            Delivered = await _db.WebhookEvents.CountAsync(e => e.Status == WebhookEventStatus.Delivered, ct),
            Failed = await _db.WebhookEvents.CountAsync(e => e.Status == WebhookEventStatus.Failed, ct),
            Dead = await _db.WebhookEvents.CountAsync(e => e.Status == WebhookEventStatus.Dead, ct),
            DueNow = await _db.WebhookEvents.CountAsync(e =>
                (e.Status == WebhookEventStatus.Pending || e.Status == WebhookEventStatus.Failed) &&
                e.NextAttemptAt <= nowUtc, ct),
            OldestPendingAt = oldestPendingAt,
            OldestNextAttemptAt = oldestNextAttemptAt
        };
    }

    private async Task<List<AdminCommandCenterWebhookItem>> GetWebhookFailuresAsync(CancellationToken ct)
    {
        return await _db.WebhookEvents
            .AsNoTracking()
            .Where(e =>
                e.Status == WebhookEventStatus.Failed ||
                e.Status == WebhookEventStatus.Dead ||
                e.Status == WebhookEventStatus.InProgress)
            .OrderByDescending(e => e.LastAttemptAt ?? e.CreatedAt)
            .Take(12)
            .Select(e => new AdminCommandCenterWebhookItem
            {
                Id = e.Id,
                ProjectId = e.ProjectId,
                ProjectName = e.Project != null ? e.Project.Name : "(deleted project)",
                EventType = e.EventType,
                Status = e.Status.ToString(),
                AttemptCount = e.AttemptCount,
                CreatedAt = e.CreatedAt,
                NextAttemptAt = e.NextAttemptAt,
                LastAttemptAt = e.LastAttemptAt,
                LastStatusCode = e.LastStatusCode,
                LastError = e.LastError
            })
            .ToListAsync(ct);
    }

    private static async Task<List<AuthsOverTimePoint>> GetAuthsOverTimeAsync(
        IQueryable<AuthEvent> authEvents,
        CancellationToken ct)
    {
        var raw = await authEvents
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

        return raw
            .Select(x => new AuthsOverTimePoint
            {
                TimestampUtc = new DateTime(x.Year, x.Month, x.Day, x.Hour, 0, 0, DateTimeKind.Local)
                    .ToUniversalTime(),
                Successful = x.Successful,
                Failed = x.Failed
            })
            .ToList();
    }

    private static async Task<List<AdminAuthEventDto>> GetRecentAuthEventsAsync(
        IQueryable<AuthEvent> authEvents,
        CancellationToken ct)
    {
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

        return rawEvents
            .Select(e => new AdminAuthEventDto
            {
                Timestamp = DateTime.SpecifyKind(e.CreatedAt, DateTimeKind.Local).ToUniversalTime(),
                ProjectId = e.ProjectId,
                ProjectName = e.ProjectName ?? "(deleted project)",
                EventType = e.EventType.ToString(),
                Success = e.Success,
                SatsPaid = e.SatsPaid,
                Reason = e.Reason,
                ClientIpMasked = MaskIp(e.ClientIp)
            })
            .ToList();
    }

    private static List<AdminCommandCenterAlert> BuildAttention(
        AdminCommandCenterAuth auth,
        AdminCommandCenterMcp mcp,
        AdminCommandCenterL402 l402,
        AdminCommandCenterWebhooks webhooks)
    {
        var alerts = new List<AdminCommandCenterAlert>();

        AddAlert(alerts, webhooks.Dead > 0, "danger", "webhook_dead", "Dead webhooks", "Permanent delivery failures need human attention.", webhooks.Dead);
        AddAlert(alerts, webhooks.Failed > 0, "warn", "webhook_failed", "Webhook retries failing", "Events are waiting on the retry schedule.", webhooks.Failed);
        AddAlert(alerts, mcp.InactiveToolDenials > 0, "danger", "mcp_tool_inactive", "Inactive MCP tool charges", "Paid calls were denied because a tool was not Active.", mcp.InactiveToolDenials);
        AddAlert(alerts, mcp.DeniedCharges > mcp.InactiveToolDenials, "warn", "mcp_denied", "MCP charge denials", "Paid-tool calls are being denied by budget or policy.", mcp.DeniedCharges - mcp.InactiveToolDenials);
        AddAlert(alerts, auth.FailureRate >= 10 && auth.AuthRequests >= 10, "warn", "auth_failures", "Auth failure rate elevated", $"{auth.FailureRate:0.0}% of auth events failed in this window.", auth.AuthFailures);
        AddAlert(alerts, auth.RateLimitHits > 0, "info", "rate_limits", "Rate limits active", "Clients are hitting auth rate limits.", auth.RateLimitHits);
        AddAlert(alerts, auth.PendingInvoices > 0, "info", "pending_invoices", "Pending invoices", "Unpaid auth, subscription, MCP, or L402 invoices are open.", auth.PendingInvoices);
        AddAlert(alerts, l402.PurchasesPending > 0 || l402.BundlesPending > 0, "info", "l402_pending", "L402 pending payments", "L402 purchases or bundles are awaiting settlement.", l402.PurchasesPending + l402.BundlesPending);

        return alerts
            .OrderBy(a => a.Severity == "danger" ? 0 : a.Severity == "warn" ? 1 : 2)
            .ThenByDescending(a => a.Count)
            .Take(8)
            .ToList();
    }

    private static void AddAlert(
        ICollection<AdminCommandCenterAlert> alerts,
        bool condition,
        string severity,
        string kind,
        string title,
        string detail,
        long count)
    {
        if (!condition)
            return;

        alerts.Add(new AdminCommandCenterAlert
        {
            Severity = severity,
            Kind = kind,
            Title = title,
            Detail = detail,
            Count = count
        });
    }

    private static double Percent(long value, long total)
        => total > 0 ? Math.Round((double)value / total * 100, 1) : 0;

    private static double? ToUsd(long sats, double? btcUsdRate)
        => btcUsdRate.HasValue ? Math.Round(sats / 100_000_000.0 * btcUsdRate.Value, 2) : null;

    private static string MaskIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return "";
        var parts = ip.Split('.');
        return parts.Length == 4 ? $"{parts[0]}.{parts[1]}.x.x" : ip;
    }

    private sealed record CommandCenterMcpToolSeed(Guid Id, string Name, string Slug, string Status);
}
