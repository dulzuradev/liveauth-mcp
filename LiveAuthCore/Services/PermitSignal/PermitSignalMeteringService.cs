using System.Security.Claims;
using LiveAuthCore.Models.Mcp;

namespace LiveAuthCore.Services.PermitSignal;

public sealed record PermitSignalMeterResult(bool Authorized, string? Reason, int PriceSats,
    long CallsUsed, long SatsUsed, Guid? RevenueEventId, McpSignedReceipt? Receipt);

public interface IPermitSignalMeteringService
{
    Task<PermitSignalMeterResult> ChargeSuccessfulCallAsync(ClaimsPrincipal caller, string toolSlug,
        int configuredPriceSats, string? idempotencyKey, string requestId,
        IReadOnlyDictionary<string, object?> metadata, CancellationToken ct);
}

/// <summary>
/// Backwards-compatible PermitSignal facade over the shared first-party MCP meter.
/// </summary>
public sealed class PermitSignalMeteringService : IPermitSignalMeteringService
{
    private readonly IMcpToolMeteringService _meter;

    public PermitSignalMeteringService(IMcpToolMeteringService meter) => _meter = meter;

    public async Task<PermitSignalMeterResult> ChargeSuccessfulCallAsync(
        ClaimsPrincipal caller,
        string toolSlug,
        int configuredPriceSats,
        string? idempotencyKey,
        string requestId,
        IReadOnlyDictionary<string, object?> metadata,
        CancellationToken ct)
    {
        var result = await _meter.ChargeSuccessfulCallAsync(caller, toolSlug, configuredPriceSats,
            idempotencyKey, requestId, "PermitSignal", metadata, null, ct);
        return new PermitSignalMeterResult(result.Authorized, result.Reason, result.PriceSats,
            result.CallsUsed, result.SatsUsed, result.RevenueEventId, result.Receipt);
    }
}
