using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Models;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Services;

public sealed record LightningFeeSettingsSnapshot(
    int InvoiceFeeBasisPoints,
    long InvoiceMinimumFeeSats,
    int BundleMarkupBasisPoints,
    long BundleMarkupMinimumFeeSats,
    int McpPaidToolFeeBasisPoints,
    long McpPaidToolMinimumFeeSats,
    DateTime? UpdatedAt = null);

public class LightningFeeSettingsService
{
    public const int SettingsRowId = 1;

    public const int DefaultInvoiceFeeBasisPoints = 200;
    public const long DefaultInvoiceMinimumFeeSats = 1;
    public const int DefaultBundleMarkupBasisPoints = 1500;
    public const long DefaultBundleMarkupMinimumFeeSats = 1;

    public const int DefaultMcpPaidToolFeeBasisPoints = 500;
    public const long DefaultMcpPaidToolMinimumFeeSats = 1;

    private readonly LiveAuthDbContext _db;
    private readonly IConfiguration _configuration;

    public LightningFeeSettingsService(LiveAuthDbContext db, IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    public async Task<LightningFeeSettingsSnapshot> GetCurrentAsync(CancellationToken ct = default)
    {
        var settings = await _db.LightningFeeSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == SettingsRowId, ct);

        return settings == null
            ? GetFallbackSnapshot(_configuration)
            : ToSnapshot(settings);
    }

    public async Task<LightningFeeSettingsResponse> GetResponseAsync(CancellationToken ct = default)
    {
        var snapshot = await GetCurrentAsync(ct);
        return ToResponse(snapshot);
    }

    public async Task<LightningFeeSettingsResponse> UpdateAsync(
        UpdateLightningFeeSettingsRequest request,
        CancellationToken ct = default)
    {
        Validate(request);

        var settings = await _db.LightningFeeSettings
            .SingleOrDefaultAsync(s => s.Id == SettingsRowId, ct);
        var current = settings == null
            ? GetFallbackSnapshot(_configuration)
            : ToSnapshot(settings);

        var now = DateTime.UtcNow;
        if (settings == null)
        {
            settings = new LightningFeeSettings
            {
                Id = SettingsRowId,
                CreatedAt = now
            };
            _db.LightningFeeSettings.Add(settings);
        }

        settings.InvoiceFeeBasisPoints = request.InvoiceFeeBasisPoints;
        settings.InvoiceMinimumFeeSats = request.InvoiceMinimumFeeSats;
        settings.BundleMarkupBasisPoints = request.BundleMarkupBasisPoints;
        settings.BundleMarkupMinimumFeeSats = request.BundleMarkupMinimumFeeSats;
        settings.McpPaidToolFeeBasisPoints = request.McpPaidToolFeeBasisPoints ?? current.McpPaidToolFeeBasisPoints;
        settings.McpPaidToolMinimumFeeSats = request.McpPaidToolMinimumFeeSats ?? current.McpPaidToolMinimumFeeSats;
        settings.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);
        return ToResponse(ToSnapshot(settings));
    }

    public static LightningFeeSettingsSnapshot GetFallbackSnapshot(IConfiguration configuration)
    {
        return new LightningFeeSettingsSnapshot(
            InvoiceFeeBasisPoints: GetInt(
                configuration,
                DefaultInvoiceFeeBasisPoints,
                "LightningAuthFees:InvoiceFeeBps",
                "LightningFees:InvoiceFeeBps",
                "L402:InvoiceFeeBps"),
            InvoiceMinimumFeeSats: GetLong(
                configuration,
                DefaultInvoiceMinimumFeeSats,
                "LightningAuthFees:InvoiceMinimumFeeSats",
                "LightningFees:InvoiceMinimumFeeSats",
                "L402:InvoiceMinimumFeeSats"),
            BundleMarkupBasisPoints: GetInt(
                configuration,
                DefaultBundleMarkupBasisPoints,
                "LightningAuthFees:BundleMarkupBps",
                "LightningFees:BundleMarkupBps",
                "L402:BundleMarkupBps"),
            BundleMarkupMinimumFeeSats: GetLong(
                configuration,
                DefaultBundleMarkupMinimumFeeSats,
                "LightningAuthFees:BundleMarkupMinimumFeeSats",
                "LightningFees:BundleMarkupMinimumFeeSats",
                "L402:BundleMarkupMinimumFeeSats"),
            McpPaidToolFeeBasisPoints: GetInt(
                configuration,
                DefaultMcpPaidToolFeeBasisPoints,
                "LightningAuthFees:McpPaidToolFeeBps",
                "LightningFees:McpPaidToolFeeBps",
                "McpPaidToolFees:FeeBps"),
            McpPaidToolMinimumFeeSats: GetLong(
                configuration,
                DefaultMcpPaidToolMinimumFeeSats,
                "LightningAuthFees:McpPaidToolMinimumFeeSats",
                "LightningFees:McpPaidToolMinimumFeeSats",
                "McpPaidToolFees:MinimumFeeSats"));
    }

    public static LightningFeeSettingsResponse ToResponse(LightningFeeSettingsSnapshot snapshot)
    {
        return new LightningFeeSettingsResponse(
            snapshot.InvoiceFeeBasisPoints,
            snapshot.InvoiceMinimumFeeSats,
            snapshot.BundleMarkupBasisPoints,
            snapshot.BundleMarkupMinimumFeeSats,
            snapshot.McpPaidToolFeeBasisPoints,
            snapshot.McpPaidToolMinimumFeeSats,
            snapshot.UpdatedAt);
    }

    public static LightningFeeSettingsSnapshot ToSnapshot(LightningFeeSettings settings)
    {
        return new LightningFeeSettingsSnapshot(
            Math.Max(0, settings.InvoiceFeeBasisPoints),
            Math.Max(0, settings.InvoiceMinimumFeeSats),
            Math.Max(0, settings.BundleMarkupBasisPoints),
            Math.Max(0, settings.BundleMarkupMinimumFeeSats),
            Math.Max(0, settings.McpPaidToolFeeBasisPoints),
            Math.Max(0, settings.McpPaidToolMinimumFeeSats),
            settings.UpdatedAt);
    }

    public static UpdateLightningFeeSettingsRequest ToUpdateRequest(LightningFeeSettingsSnapshot snapshot)
    {
        return new UpdateLightningFeeSettingsRequest
        {
            InvoiceFeeBasisPoints = snapshot.InvoiceFeeBasisPoints,
            InvoiceMinimumFeeSats = snapshot.InvoiceMinimumFeeSats,
            BundleMarkupBasisPoints = snapshot.BundleMarkupBasisPoints,
            BundleMarkupMinimumFeeSats = snapshot.BundleMarkupMinimumFeeSats,
            McpPaidToolFeeBasisPoints = snapshot.McpPaidToolFeeBasisPoints,
            McpPaidToolMinimumFeeSats = snapshot.McpPaidToolMinimumFeeSats
        };
    }

    public async Task<(int PlatformFeeSats, int NetSats, int FeeBasisPoints)> CalculateMcpPaidToolFeeAsync(
        int grossSats,
        CancellationToken ct = default)
    {
        var settings = await GetCurrentAsync(ct);
        var platformFeeSats = (int)BasisPointFeeMath.CalculateFeeSats(
            grossSats,
            settings.McpPaidToolFeeBasisPoints,
            settings.McpPaidToolMinimumFeeSats);

        return (platformFeeSats, grossSats - platformFeeSats, settings.McpPaidToolFeeBasisPoints);
    }

    private static int GetInt(IConfiguration configuration, int fallback, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = configuration.GetValue<int?>(key);
            if (value.HasValue)
                return Math.Max(0, value.Value);
        }

        return fallback;
    }

    private static long GetLong(IConfiguration configuration, long fallback, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = configuration.GetValue<long?>(key);
            if (value.HasValue)
                return Math.Max(0, value.Value);
        }

        return fallback;
    }

    private static void Validate(UpdateLightningFeeSettingsRequest request)
    {
        if (request.InvoiceFeeBasisPoints < 0)
            throw new ArgumentOutOfRangeException(nameof(request.InvoiceFeeBasisPoints), "Invoice fee bps must be zero or greater.");

        if (request.InvoiceMinimumFeeSats < 0)
            throw new ArgumentOutOfRangeException(nameof(request.InvoiceMinimumFeeSats), "Invoice minimum fee sats must be zero or greater.");

        if (request.BundleMarkupBasisPoints < 0)
            throw new ArgumentOutOfRangeException(nameof(request.BundleMarkupBasisPoints), "Bundle markup bps must be zero or greater.");

        if (request.BundleMarkupMinimumFeeSats < 0)
            throw new ArgumentOutOfRangeException(nameof(request.BundleMarkupMinimumFeeSats), "Bundle markup minimum fee sats must be zero or greater.");

        if (request.McpPaidToolFeeBasisPoints is < 0)
            throw new ArgumentOutOfRangeException(nameof(request.McpPaidToolFeeBasisPoints), "MCP paid tool fee bps must be zero or greater.");

        if (request.McpPaidToolMinimumFeeSats is < 0)
            throw new ArgumentOutOfRangeException(nameof(request.McpPaidToolMinimumFeeSats), "MCP paid tool minimum fee sats must be zero or greater.");
    }
}
