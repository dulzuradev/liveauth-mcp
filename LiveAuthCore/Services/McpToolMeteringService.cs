using System.Security.Claims;
using System.Text.Json;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Data.Entities.Mcp;
using LiveAuthCore.Models.Mcp;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Services;

public sealed record McpToolMeterResult(
    bool Authorized,
    string? Reason,
    int PriceSats,
    long CallsUsed,
    long SatsUsed,
    Guid? RevenueEventId,
    McpSignedReceipt? Receipt,
    bool Duplicate = false,
    Guid? ReservationId = null);

public interface IMcpToolMeteringService
{
    Task<McpToolMeterResult> ChargeSuccessfulCallAsync(
        ClaimsPrincipal caller,
        string toolSlug,
        int configuredPriceSats,
        string? idempotencyKey,
        string requestId,
        string product,
        IReadOnlyDictionary<string, object?> metadata,
        McpReceiptAttestation? attestation,
        CancellationToken ct);

    Task<McpToolMeterResult> ReserveCallAsync(
        ClaimsPrincipal caller,
        string toolSlug,
        int configuredPriceSats,
        string? idempotencyKey,
        string requestId,
        string product,
        IReadOnlyDictionary<string, object?> metadata,
        CancellationToken ct);

    Task<McpToolMeterResult> CommitReservationAsync(
        Guid reservationId,
        IReadOnlyDictionary<string, object?> metadata,
        McpReceiptAttestation? attestation,
        CancellationToken ct);

    Task CancelReservationAsync(Guid reservationId, string reason, CancellationToken ct);
}

/// <summary>
/// Shared first-party MCP charging path. It deliberately reserves broadcast charges
/// before a non-idempotent side effect and signs the final receipt only after success.
/// </summary>
public sealed class McpToolMeteringService : IMcpToolMeteringService
{
    private readonly LiveAuthDbContext _db;
    private readonly LightningFeeSettingsService _fees;
    private readonly McpReceiptService _receipts;
    private readonly WebhookService _webhooks;
    private readonly ILogger<McpToolMeteringService> _logger;

    public McpToolMeteringService(
        LiveAuthDbContext db,
        LightningFeeSettingsService fees,
        McpReceiptService receipts,
        WebhookService webhooks,
        ILogger<McpToolMeteringService> logger)
    {
        _db = db;
        _fees = fees;
        _receipts = receipts;
        _webhooks = webhooks;
        _logger = logger;
    }

    public async Task<McpToolMeterResult> ChargeSuccessfulCallAsync(
        ClaimsPrincipal caller,
        string toolSlug,
        int configuredPriceSats,
        string? idempotencyKey,
        string requestId,
        string product,
        IReadOnlyDictionary<string, object?> metadata,
        McpReceiptAttestation? attestation,
        CancellationToken ct)
    {
        var reservation = await ReserveCallAsync(caller, toolSlug, configuredPriceSats,
            idempotencyKey, requestId, product, metadata, ct);
        if (!reservation.Authorized || reservation.ReservationId == null)
            return reservation;
        if (reservation.Duplicate)
        {
            if (attestation == null || reservation.RevenueEventId == null) return reservation;
            var existing = await _db.McpToolRevenueEvents.AsNoTracking()
                .SingleAsync(item => item.Id == reservation.RevenueEventId.Value, ct);
            var existingTool = await _db.McpTools.AsNoTracking()
                .SingleAsync(item => item.Id == existing.McpToolId, ct);
            return reservation with { Receipt = _receipts.CreateReceipt(existing, existingTool, attestation) };
        }

        return await CommitReservationAsync(reservation.ReservationId.Value, metadata, attestation, ct);
    }

    public async Task<McpToolMeterResult> ReserveCallAsync(
        ClaimsPrincipal caller,
        string toolSlug,
        int configuredPriceSats,
        string? idempotencyKey,
        string requestId,
        string product,
        IReadOnlyDictionary<string, object?> metadata,
        CancellationToken ct)
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

        var project = await _db.Projects
            .SingleOrDefaultAsync(item => item.Id == projectId && item.IsActive && !item.IsDeleted, ct);
        if (project == null) return Denied("project_inactive", configuredPriceSats, gateToken);

        var tool = await _db.McpTools
            .SingleOrDefaultAsync(item => item.Slug == toolSlug && item.RemovedAt == null, ct);
        if (tool == null || !string.Equals(tool.Status, "Active", StringComparison.OrdinalIgnoreCase))
            return Denied("tool_inactive", configuredPriceSats, gateToken);

        // The database-seeded tool is the pricing authority. Configuration updates the
        // seed, while this guard prevents a stale caller from silently changing price.
        var configured = Math.Clamp(configuredPriceSats, 1, 1_000_000);
        var price = Math.Clamp(tool.DefaultCostSats > 0 ? tool.DefaultCostSats : configured, 1, 1_000_000);
        var storedIdempotencyKey = NormalizeIdempotencyKey(project.Id, idempotencyKey);
        if (storedIdempotencyKey != null)
        {
            var existing = await FindExistingAsync(tool.Id, storedIdempotencyKey, ct);
            if (existing != null)
                return ExistingResult(existing, tool, gateToken);
        }

        if (gateToken.DayWindowStart.Date != DateTime.UtcNow.Date)
        {
            gateToken.DayWindowStart = DateTime.UtcNow.Date;
            gateToken.SatsUsed = 0;
            gateToken.CallsUsed = 0;
        }

        var balanceDeducted = project.L402BalanceSats >= price;
        if (balanceDeducted)
            project.L402BalanceSats -= price;
        else if (gateToken.SatsUsed + price > gateToken.MaxSatsPerDay)
        {
            await RecordDeniedAsync(tool, gateToken, project, price, requestId, metadata,
                "budget_exceeded", ct);
            await transaction.CommitAsync(ct);
            return Denied("budget_exceeded", price, gateToken);
        }

        gateToken.CallsUsed++;
        gateToken.SatsUsed += price;
        var reservationMetadata = new Dictionary<string, object?>(metadata)
        {
            ["product"] = product,
            ["balanceDeducted"] = balanceDeducted,
            ["reservedAt"] = DateTime.UtcNow
        };
        var revenue = new McpToolRevenueEvent
        {
            McpToolId = tool.Id,
            McpGateTokenId = gateToken.Id,
            McpGateSessionId = gateToken.SessionId,
            PayingProjectId = project.Id,
            AgentId = caller.FindFirst("sub")?.Value ?? caller.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            ToolMethodName = toolSlug,
            GrossSats = price,
            Status = "Reserved",
            IdempotencyKey = storedIdempotencyKey,
            RequestId = requestId,
            MetadataJson = JsonSerializer.Serialize(reservationMetadata),
            CreatedAt = DateTime.UtcNow
        };
        _db.McpToolRevenueEvents.Add(revenue);

        try
        {
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException) when (storedIdempotencyKey != null)
        {
            await transaction.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            var concurrent = await FindExistingAsync(tool.Id, storedIdempotencyKey, ct);
            if (concurrent != null)
            {
                var tokenSnapshot = await _db.McpGateTokens.AsNoTracking()
                    .SingleAsync(item => item.Id == gateToken.Id, ct);
                return ExistingResult(concurrent, tool, tokenSnapshot);
            }
            throw;
        }

        return new McpToolMeterResult(true, null, price, gateToken.CallsUsed,
            gateToken.SatsUsed, revenue.Id, null, ReservationId: revenue.Id);
    }

    public async Task<McpToolMeterResult> CommitReservationAsync(
        Guid reservationId,
        IReadOnlyDictionary<string, object?> metadata,
        McpReceiptAttestation? attestation,
        CancellationToken ct)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        var revenue = await _db.McpToolRevenueEvents.SingleOrDefaultAsync(item => item.Id == reservationId, ct)
            ?? throw new InvalidOperationException("MCP charge reservation was not found.");
        var tool = await _db.McpTools.SingleAsync(item => item.Id == revenue.McpToolId, ct);
        var token = revenue.McpGateTokenId.HasValue
            ? await _db.McpGateTokens.SingleAsync(item => item.Id == revenue.McpGateTokenId.Value, ct)
            : null;

        if (string.Equals(revenue.Status, "Charged", StringComparison.OrdinalIgnoreCase))
            return new McpToolMeterResult(true, null, revenue.GrossSats,
                token?.CallsUsed ?? 0, token?.SatsUsed ?? 0, revenue.Id,
                _receipts.CreateReceipt(revenue, tool, attestation), true);
        if (!string.Equals(revenue.Status, "Reserved", StringComparison.OrdinalIgnoreCase))
            return Denied("reservation_not_active", revenue.GrossSats, token);

        var fee = await _fees.CalculateMcpPaidToolFeeAsync(revenue.GrossSats, ct);
        revenue.PlatformFeeSats = fee.PlatformFeeSats;
        revenue.NetSats = fee.NetSats;
        revenue.FeeBasisPoints = fee.FeeBasisPoints;
        revenue.Status = "Charged";
        revenue.MetadataJson = JsonSerializer.Serialize(metadata);
        await _db.SaveChangesAsync(ct);

        // Signing is inside the transaction: a call cannot be charged without a receipt.
        var receipt = _receipts.CreateReceipt(revenue, tool, attestation);
        await transaction.CommitAsync(ct);

        // Once the ledger transaction commits, client cancellation must not turn a
        // successful paid call into an apparent failure during best-effort delivery.
        var completionCt = CancellationToken.None;
        var project = revenue.PayingProjectId.HasValue
            ? await _db.Projects.AsNoTracking().SingleOrDefaultAsync(
                item => item.Id == revenue.PayingProjectId.Value, completionCt)
            : null;
        if (project != null)
            await EnqueueWebhookBestEffortAsync(project, tool, revenue, receipt,
                ProductFrom(metadata), completionCt);

        return new McpToolMeterResult(true, null, revenue.GrossSats,
            token?.CallsUsed ?? 0, token?.SatsUsed ?? 0, revenue.Id, receipt);
    }

    public async Task CancelReservationAsync(Guid reservationId, string reason, CancellationToken ct)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        var revenue = await _db.McpToolRevenueEvents.SingleOrDefaultAsync(item => item.Id == reservationId, ct);
        if (revenue == null || !string.Equals(revenue.Status, "Reserved", StringComparison.OrdinalIgnoreCase))
            return;

        var balanceDeducted = MetadataBoolean(revenue.MetadataJson, "balanceDeducted");
        if (revenue.McpGateTokenId.HasValue)
        {
            var token = await _db.McpGateTokens.SingleOrDefaultAsync(item => item.Id == revenue.McpGateTokenId.Value, ct);
            if (token != null)
            {
                token.CallsUsed = Math.Max(0, token.CallsUsed - 1);
                token.SatsUsed = Math.Max(0, token.SatsUsed - revenue.GrossSats);
            }
        }
        if (balanceDeducted && revenue.PayingProjectId.HasValue)
        {
            var project = await _db.Projects.SingleOrDefaultAsync(item => item.Id == revenue.PayingProjectId.Value, ct);
            if (project != null) project.L402BalanceSats += revenue.GrossSats;
        }

        var cancelledIdempotencyKey = revenue.IdempotencyKey;
        revenue.Status = "Cancelled";
        revenue.IdempotencyKey = null; // A retry may reserve and attempt the operation again.
        revenue.MetadataJson = JsonSerializer.Serialize(new
        {
            cancelReason = reason,
            idempotencyKey = cancelledIdempotencyKey,
            cancelledAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task<McpToolRevenueEvent?> FindExistingAsync(Guid toolId, string idempotencyKey, CancellationToken ct)
        => await _db.McpToolRevenueEvents.AsNoTracking()
            .SingleOrDefaultAsync(item => item.McpToolId == toolId &&
                                          item.IdempotencyKey == idempotencyKey &&
                                          (item.Status == "Charged" || item.Status == "Reserved"), ct);

    private McpToolMeterResult ExistingResult(McpToolRevenueEvent existing, McpTool tool, McpGateToken token)
    {
        if (string.Equals(existing.Status, "Charged", StringComparison.OrdinalIgnoreCase))
            return new McpToolMeterResult(true, null, existing.GrossSats, token.CallsUsed,
                token.SatsUsed, existing.Id, _receipts.CreateReceipt(existing, tool), true);
        return new McpToolMeterResult(false, "call_in_progress", existing.GrossSats,
            token.CallsUsed, token.SatsUsed, existing.Id, null, true, existing.Id);
    }

    private async Task RecordDeniedAsync(
        McpTool tool,
        McpGateToken token,
        Project project,
        int price,
        string requestId,
        IReadOnlyDictionary<string, object?> metadata,
        string reason,
        CancellationToken ct)
    {
        _db.McpToolRevenueEvents.Add(new McpToolRevenueEvent
        {
            McpToolId = tool.Id,
            McpGateTokenId = token.Id,
            McpGateSessionId = token.SessionId,
            PayingProjectId = project.Id,
            ToolMethodName = tool.Slug,
            GrossSats = price,
            Status = "Denied",
            RequestId = requestId,
            MetadataJson = JsonSerializer.Serialize(new { denyReason = reason, metadata }),
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
    }

    private async Task EnqueueWebhookBestEffortAsync(
        Project project,
        McpTool tool,
        McpToolRevenueEvent revenue,
        McpSignedReceipt receipt,
        string product,
        CancellationToken ct)
    {
        try
        {
            await _webhooks.EnqueueAsync(project, "liveauth.mcp.tool.paid_call", new
            {
                type = "liveauth.mcp.tool.paid_call",
                product,
                createdAt = revenue.CreatedAt,
                projectId = project.Id,
                mcpToolId = tool.Id,
                toolName = tool.Name,
                toolSlug = tool.Slug,
                revenueEventId = revenue.Id,
                grossSats = revenue.GrossSats,
                platformFeeSats = revenue.PlatformFeeSats,
                netSats = revenue.NetSats,
                receipt
            }, tool.WebhookUrl, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "MCP paid call {RevenueEventId} succeeded but its webhook could not be queued.", revenue.Id);
        }
    }

    private static McpToolMeterResult Denied(string reason, int price, McpGateToken? token = null)
        => new(false, reason, Math.Max(1, price), token?.CallsUsed ?? 0, token?.SatsUsed ?? 0, null, null);

    private static string? NormalizeIdempotencyKey(Guid projectId, string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var trimmed = key.Trim();
        if (trimmed.Length > 160) trimmed = trimmed[..160];
        return $"{projectId:N}:{trimmed}";
    }

    private static bool MetadataBoolean(string? json, string name)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string ProductFrom(IReadOnlyDictionary<string, object?> metadata)
        => metadata.TryGetValue("product", out var value) && value is string product && !string.IsNullOrWhiteSpace(product)
            ? product
            : "LiveAuth";
}
