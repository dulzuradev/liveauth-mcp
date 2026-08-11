using System.Text.Json;
using LiveAuthCore.Bitcoin.Configuration;
using LiveAuthCore.Bitcoin.Rpc;
using LiveAuthCore.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Bitcoin.Controllers;

[ApiController]
[Route("api/admin/bitcoin-gateway")]
[Authorize(Roles = "Admin")]
public sealed class BitcoinGatewayAdminController : ControllerBase
{
    private static readonly string[] ToolSlugs =
    [
        BitcoinGatewayTools.FeeEstimates,
        BitcoinGatewayTools.MempoolSummary,
        BitcoinGatewayTools.PreflightTransaction,
        BitcoinGatewayTools.BroadcastTransaction,
        BitcoinGatewayTools.TransactionStatus
    ];

    private readonly LiveAuthDbContext _db;
    private readonly BitcoinRpcCircuitBreaker _circuit;

    public BitcoinGatewayAdminController(LiveAuthDbContext db, BitcoinRpcCircuitBreaker circuit)
    {
        _db = db;
        _circuit = circuit;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int windowHours = 24, CancellationToken ct = default)
    {
        var hours = Math.Clamp(windowHours, 1, 24 * 90);
        var since = DateTime.UtcNow.AddHours(-hours);
        var tools = await _db.McpTools.AsNoTracking()
            .Where(tool => ToolSlugs.Contains(tool.Slug))
            .Select(tool => new { tool.Id, tool.Name, tool.Slug, tool.DefaultCostSats })
            .ToListAsync(ct);
        var toolIds = tools.Select(tool => tool.Id).ToArray();
        var events = await _db.McpToolRevenueEvents.AsNoTracking()
            .Where(item => toolIds.Contains(item.McpToolId) && item.CreatedAt >= since)
            .ToListAsync(ct);
        var operations = await _db.BitcoinGatewayOperations.AsNoTracking()
            .Where(item => item.CreatedAt >= since)
            .ToListAsync(ct);

        var charged = events.Where(item => item.Status == "Charged").ToArray();
        var parsed = charged.Select(item => ParseMetadata(item.MetadataJson)).ToArray();
        var durations = parsed.Select(item => item.DurationMilliseconds).Where(value => value.HasValue)
            .Select(value => value!.Value).ToArray();
        var rpcDurations = parsed.Select(item => item.RpcLatencyMilliseconds).Where(value => value.HasValue)
            .Select(value => value!.Value).ToArray();
        var cacheObservations = parsed.Where(item => item.CacheHit.HasValue).ToArray();
        var preflights = parsed.Where(item => item.Tool == BitcoinGatewayTools.PreflightTransaction &&
                                              item.Accepted.HasValue).ToArray();

        return Ok(new
        {
            windowHours = hours,
            generatedAt = DateTime.UtcNow,
            calls = new
            {
                successful = charged.LongLength,
                denied = events.LongCount(item => item.Status == "Denied"),
                cancelled = events.LongCount(item => item.Status == "Cancelled"),
                satsGenerated = charged.Sum(item => (long)item.GrossSats),
                uniqueProjects = charged.Where(item => item.PayingProjectId.HasValue)
                    .Select(item => item.PayingProjectId).Distinct().LongCount(),
                uniqueClients = charged.Where(item => !string.IsNullOrWhiteSpace(item.AgentId))
                    .Select(item => item.AgentId).Distinct().LongCount()
            },
            broadcasts = new
            {
                attempted = operations.LongCount(),
                accepted = operations.LongCount(item => item.Status == "Succeeded"),
                rejected = operations.LongCount(item => item.Status == "Rejected"),
                retryableFailures = operations.LongCount(item => item.Status == "RetryableFailed"),
                processing = operations.LongCount(item => item.Status == "Processing")
            },
            preflight = new
            {
                accepted = preflights.LongCount(item => item.Accepted == true),
                rejected = preflights.LongCount(item => item.Accepted == false)
            },
            performance = new
            {
                averageLatencyMilliseconds = durations.Length == 0 ? 0 : durations.Average(),
                averageBitcoinRpcLatencyMilliseconds = rpcDurations.Length == 0 ? 0 : rpcDurations.Average(),
                cacheHitRatio = cacheObservations.Length == 0 ? 0 :
                    (double)cacheObservations.LongCount(item => item.CacheHit == true) / cacheObservations.LongLength,
                circuitBreakerOpenEvents = _circuit.OpenEvents
            },
            tools = tools.Select(tool => new
            {
                tool.Name,
                tool.Slug,
                priceSats = tool.DefaultCostSats,
                calls = charged.LongCount(item => item.McpToolId == tool.Id),
                failedOrDenied = events.LongCount(item => item.McpToolId == tool.Id && item.Status != "Charged"),
                satsGenerated = charged.Where(item => item.McpToolId == tool.Id).Sum(item => (long)item.GrossSats)
            })
        });
    }

    private static ParsedMetadata ParseMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ParsedMetadata();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var parsed = new ParsedMetadata
            {
                Tool = String(root, "tool"),
                DurationMilliseconds = Integer(root, "durationMilliseconds"),
                RpcLatencyMilliseconds = Integer(root, "bitcoinRpcLatencyMilliseconds")
            };
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Object)
            {
                parsed.Accepted = Boolean(details, "accepted");
                parsed.CacheHit = Boolean(details, "cacheHit");
            }
            return parsed;
        }
        catch (JsonException)
        {
            return new ParsedMetadata();
        }
    }

    private static string? String(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    private static long? Integer(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : null;
    private static bool? Boolean(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private sealed class ParsedMetadata
    {
        public string? Tool { get; init; }
        public long? DurationMilliseconds { get; init; }
        public long? RpcLatencyMilliseconds { get; init; }
        public bool? Accepted { get; set; }
        public bool? CacheHit { get; set; }
    }
}
