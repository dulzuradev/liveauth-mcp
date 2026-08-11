using System.Security.Claims;
using System.Text.Json;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Data.Entities.Mcp;
using LiveAuthCore.Models.Mcp;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Services.PermitSignal;

public sealed record PermitSignalMeterResult(bool Authorized, string? Reason, int PriceSats,
    long CallsUsed, long SatsUsed, Guid? RevenueEventId, McpSignedReceipt? Receipt);

public interface IPermitSignalMeteringService
{
    Task<PermitSignalMeterResult> ChargeSuccessfulCallAsync(ClaimsPrincipal caller, string toolSlug,
        int configuredPriceSats, string? idempotencyKey, string requestId,
        IReadOnlyDictionary<string, object?> metadata, CancellationToken ct);
}

public sealed class PermitSignalMeteringService : IPermitSignalMeteringService
{
    private readonly LiveAuthDbContext _db;
    private readonly LightningFeeSettingsService _fees;
    private readonly McpReceiptService _receipts;
    private readonly WebhookService _webhooks;
    private readonly ILogger<PermitSignalMeteringService> _logger;

    public PermitSignalMeteringService(LiveAuthDbContext db, LightningFeeSettingsService fees,
        McpReceiptService receipts, WebhookService webhooks, ILogger<PermitSignalMeteringService> logger)
    {
        _db = db;
        _fees = fees;
        _receipts = receipts;
        _webhooks = webhooks;
        _logger = logger;
    }

    public async Task<PermitSignalMeterResult> ChargeSuccessfulCallAsync(ClaimsPrincipal caller,
        string toolSlug, int configuredPriceSats, string? idempotencyKey, string requestId,
        IReadOnlyDictionary<string, object?> metadata, CancellationToken ct)
    {
        if (!Guid.TryParse(caller.FindFirst("projectId")?.Value, out var projectId) ||
            string.IsNullOrWhiteSpace(caller.FindFirst("jti")?.Value))
            return Denied("missing_mcp_identity", configuredPriceSats);

        var jti = caller.FindFirst("jti")!.Value;
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        var gateToken = await _db.McpGateTokens
            .SingleOrDefaultAsync(token => token.ProjectId == projectId && token.JwtId == jti && token.Status == "active", ct);
        if (gateToken == null) return Denied("unknown_token", configuredPriceSats);
        if (gateToken.ExpiresAt < DateTime.UtcNow) return Denied("token_expired", configuredPriceSats, gateToken);

        var project = await _db.Projects.SingleOrDefaultAsync(item => item.Id == projectId && item.IsActive && !item.IsDeleted, ct);
        if (project == null) return Denied("project_inactive", configuredPriceSats, gateToken);
        var tool = await _db.McpTools.SingleOrDefaultAsync(item => item.Slug == toolSlug && item.RemovedAt == null, ct);
        if (tool == null || !string.Equals(tool.Status, "Active", StringComparison.OrdinalIgnoreCase))
            return Denied("tool_inactive", configuredPriceSats, gateToken);

        var price = Math.Clamp(configuredPriceSats, 1, 1_000_000);
        var storedIdempotencyKey = NormalizeIdempotencyKey(gateToken.Id, idempotencyKey);
        if (storedIdempotencyKey != null)
        {
            var existing = await _db.McpToolRevenueEvents.AsNoTracking()
                .SingleOrDefaultAsync(item => item.McpToolId == tool.Id &&
                                              item.IdempotencyKey == storedIdempotencyKey &&
                                              item.McpGateTokenId == gateToken.Id && item.Status == "Charged", ct);
            if (existing != null)
                return new PermitSignalMeterResult(true, null, existing.GrossSats, gateToken.CallsUsed,
                    gateToken.SatsUsed, existing.Id, _receipts.CreateReceipt(existing, tool));
        }

        if (gateToken.DayWindowStart.Date != DateTime.UtcNow.Date)
        {
            gateToken.DayWindowStart = DateTime.UtcNow.Date;
            gateToken.SatsUsed = 0;
            gateToken.CallsUsed = 0;
        }

        if (project.L402BalanceSats >= price)
            project.L402BalanceSats -= price;
        else if (gateToken.SatsUsed + price > gateToken.MaxSatsPerDay)
        {
            await RecordDeniedAsync(tool, gateToken, project, price, storedIdempotencyKey,
                requestId, metadata, "budget_exceeded", ct);
            await transaction.CommitAsync(ct);
            return Denied("budget_exceeded", price, gateToken);
        }

        gateToken.CallsUsed++;
        gateToken.SatsUsed += price;
        var fee = await _fees.CalculateMcpPaidToolFeeAsync(price, ct);
        var revenue = new McpToolRevenueEvent
        {
            McpToolId = tool.Id,
            McpGateTokenId = gateToken.Id,
            McpGateSessionId = gateToken.SessionId,
            PayingProjectId = project.Id,
            AgentId = caller.FindFirst("sub")?.Value ?? caller.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            ToolMethodName = toolSlug,
            GrossSats = price,
            PlatformFeeSats = fee.PlatformFeeSats,
            NetSats = fee.NetSats,
            FeeBasisPoints = fee.FeeBasisPoints,
            Status = "Charged",
            IdempotencyKey = storedIdempotencyKey,
            RequestId = requestId,
            MetadataJson = JsonSerializer.Serialize(metadata),
            CreatedAt = DateTime.UtcNow
        };
        _db.McpToolRevenueEvents.Add(revenue);
        await _db.SaveChangesAsync(ct);
        // Receipt generation is part of the successful paid-call transaction. If signing
        // fails, the transaction rolls back instead of charging without a usable receipt.
        var receipt = _receipts.CreateReceipt(revenue, tool);
        await transaction.CommitAsync(ct);

        await EnqueueWebhookBestEffortAsync(project, tool, revenue, receipt, ct);
        return new PermitSignalMeterResult(true, null, price, gateToken.CallsUsed,
            gateToken.SatsUsed, revenue.Id, receipt);
    }

    private async Task RecordDeniedAsync(McpTool tool, McpGateToken token, Project project, int price,
        string? idempotencyKey, string requestId, IReadOnlyDictionary<string, object?> metadata,
        string reason, CancellationToken ct)
    {
        _db.McpToolRevenueEvents.Add(new McpToolRevenueEvent
        {
            McpToolId = tool.Id, McpGateTokenId = token.Id, McpGateSessionId = token.SessionId,
            PayingProjectId = project.Id, ToolMethodName = tool.Slug, GrossSats = price,
            Status = "Denied", IdempotencyKey = null, RequestId = requestId,
            MetadataJson = JsonSerializer.Serialize(new { denyReason = reason, metadata }), CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
    }

    private async Task EnqueueWebhookBestEffortAsync(Project project, McpTool tool,
        McpToolRevenueEvent revenue, McpSignedReceipt receipt, CancellationToken ct)
    {
        try
        {
            await _webhooks.EnqueueAsync(project, "liveauth.mcp.tool.paid_call", new
            {
                type = "liveauth.mcp.tool.paid_call", product = "PermitSignal", createdAt = revenue.CreatedAt,
                projectId = project.Id, mcpToolId = tool.Id, toolName = tool.Name, toolSlug = tool.Slug,
                revenueEventId = revenue.Id, grossSats = revenue.GrossSats,
                platformFeeSats = revenue.PlatformFeeSats, netSats = revenue.NetSats, receipt
            }, tool.WebhookUrl, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "PermitSignal paid call {RevenueEventId} succeeded but its webhook could not be queued.", revenue.Id);
        }
    }

    private static PermitSignalMeterResult Denied(string reason, int price, McpGateToken? token = null)
        => new(false, reason, Math.Max(1, price), token?.CallsUsed ?? 0, token?.SatsUsed ?? 0, null, null);

    private static string? NormalizeIdempotencyKey(Guid tokenId, string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var trimmed = key.Trim();
        if (trimmed.Length > 160) trimmed = trimmed[..160];
        return $"{tokenId:N}:{trimmed}";
    }
}
