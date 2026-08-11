using System.Text.Json;
using LiveAuthCore.Bitcoin.Configuration;
using LiveAuthCore.Bitcoin.Models;
using LiveAuthCore.Bitcoin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace LiveAuthCore.Bitcoin.Controllers;

[ApiController]
[Route("api/bitcoin/mcp")]
[Authorize(Roles = "McpClient")]
[EnableRateLimiting("bitcoin-gateway")]
[RequestSizeLimit(8_100_000)] // hard ceiling for the 4 MB raw-byte safety maximum encoded as hex/JSON
public sealed class BitcoinMcpController : ControllerBase
{
    private const string ProtocolVersion = "2025-06-18";
    private readonly IBitcoinGatewayExecutionService _gateway;
    private readonly BitcoinGatewayToolOptions _prices;
    private readonly ILogger<BitcoinMcpController> _logger;

    public BitcoinMcpController(
        IBitcoinGatewayExecutionService gateway,
        IOptions<BitcoinGatewayOptions> options,
        ILogger<BitcoinMcpController> logger)
    {
        _gateway = gateway;
        _prices = options.Value.Tools;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Handle([FromBody] BitcoinMcpJsonRpcRequest request, CancellationToken ct)
    {
        if (request.Jsonrpc != "2.0") return RpcError(request.Id, -32600, "jsonrpc must be '2.0'.");
        if (request.Method == "notifications/initialized") return NoContent();
        try
        {
            return request.Method switch
            {
                "initialize" => RpcResult(request.Id, new
                {
                    protocolVersion = ProtocolVersion,
                    capabilities = new { tools = new { listChanged = false } },
                    serverInfo = new { name = "LiveAuth Bitcoin Agent Gateway", version = "1.0.0" },
                    instructions = "Node-backed, metered Bitcoin infrastructure. LiveAuth never accepts keys, signs transactions, or creates wallets. Preflight never broadcasts; broadcast can submit a signed raw transaction."
                }),
                "ping" => RpcResult(request.Id, new { }),
                "tools/list" => RpcResult(request.Id, new { tools = ToolDefinitions() }),
                "tools/call" => await CallToolAsync(request, ct),
                _ => RpcError(request.Id, -32601, $"Unknown method '{request.Method}'.")
            };
        }
        catch (BitcoinGatewayException ex)
        {
            return RpcError(request.Id, -32010, ex.Message, new
            {
                code = ex.Code,
                retryable = ex.Retryable,
                httpStatus = ex.StatusCode,
                retryAfterSeconds = ex.RetryAfterSeconds,
                requestId = HttpContext.TraceIdentifier
            });
        }
        catch (JsonException ex)
        {
            return RpcError(request.Id, -32602, $"Invalid tool arguments: {ex.Message}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bitcoin MCP method {Method} failed before metering completed.", request.Method);
            return RpcError(request.Id, -32603,
                "LiveAuth could not complete the Bitcoin tool call. The call was not charged.",
                new { code = "LIVEAUTH_BITCOIN_INTERNAL_ERROR", retryable = true });
        }
    }

    private async Task<IActionResult> CallToolAsync(BitcoinMcpJsonRpcRequest request, CancellationToken ct)
    {
        if (!request.Params.HasValue || request.Params.Value.ValueKind != JsonValueKind.Object)
            return RpcError(request.Id, -32602, "tools/call requires params.");
        var parameters = request.Params.Value;
        if (!parameters.TryGetProperty("name", out var nameElement) ||
            nameElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(nameElement.GetString()))
            return RpcError(request.Id, -32602, "Tool name is required.");
        RejectUnknownProperties(parameters, "name", "arguments");
        var name = nameElement.GetString()!;
        var arguments = parameters.TryGetProperty("arguments", out var argumentElement)
            ? argumentElement
            : EmptyObject();
        if (arguments.ValueKind != JsonValueKind.Object)
            return RpcError(request.Id, -32602, "Tool arguments must be an object.");
        var idempotencyKey = Request.Headers["X-LiveAuth-Idempotency-Key"].FirstOrDefault();
        var requestId = HttpContext.TraceIdentifier;

        return name switch
        {
            BitcoinGatewayTools.FeeEstimates => await InvokeAsync(request.Id,
                NoArguments(arguments, () => _gateway.GetFeeEstimatesAsync(User, idempotencyKey, requestId, ct))),
            BitcoinGatewayTools.MempoolSummary => await InvokeAsync(request.Id,
                NoArguments(arguments, () => _gateway.GetMempoolSummaryAsync(User, idempotencyKey, requestId, ct))),
            BitcoinGatewayTools.PreflightTransaction => await InvokeAsync(request.Id,
                _gateway.PreflightAsync(User, RawTransaction(arguments), idempotencyKey, requestId, ct)),
            BitcoinGatewayTools.BroadcastTransaction => await InvokeAsync(request.Id,
                _gateway.BroadcastAsync(User, RawTransaction(arguments), idempotencyKey, requestId, ct)),
            BitcoinGatewayTools.TransactionStatus => await InvokeAsync(request.Id,
                _gateway.GetTransactionStatusAsync(User, TransactionId(arguments), idempotencyKey, requestId, ct)),
            _ => RpcError(request.Id, -32602, $"Unknown Bitcoin Gateway tool '{name}'.")
        };
    }

    private Task<IActionResult> InvokeAsync<T>(JsonElement? id, Task<BitcoinPaidResult<T>> pending)
        => InvokeCoreAsync(id, pending);

    private async Task<IActionResult> InvokeCoreAsync<T>(JsonElement? id, Task<BitcoinPaidResult<T>> pending)
    {
        var result = await pending;
        var serialized = JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return RpcResult(id, new Dictionary<string, object?>
        {
            ["content"] = new[] { new { type = "text", text = serialized } },
            ["structuredContent"] = result.Value,
            ["isError"] = false,
            ["_meta"] = new
            {
                liveauth = new
                {
                    paid = result.PriceSats > 0,
                    priceSats = result.PriceSats,
                    revenueEventId = result.RevenueEventId,
                    idempotentReplay = result.Duplicate
                }
            }
        });
    }

    private static async Task<BitcoinPaidResult<T>> NoArguments<T>(
        JsonElement arguments,
        Func<Task<BitcoinPaidResult<T>>> action)
    {
        RejectUnknownProperties(arguments);
        return await action();
    }

    private static string RawTransaction(JsonElement arguments)
    {
        RejectUnknownProperties(arguments, "rawTransaction");
        if (!arguments.TryGetProperty("rawTransaction", out var raw) || raw.ValueKind != JsonValueKind.String)
            throw new BitcoinGatewayException(BitcoinErrorCodes.InvalidTransaction,
                "rawTransaction is required and must be a hexadecimal string.");
        return raw.GetString() ?? string.Empty;
    }

    private static string TransactionId(JsonElement arguments)
    {
        RejectUnknownProperties(arguments, "txid");
        if (!arguments.TryGetProperty("txid", out var txid) || txid.ValueKind != JsonValueKind.String)
            throw new BitcoinGatewayException(BitcoinErrorCodes.InvalidTransaction,
                "txid is required and must be a hexadecimal string.");
        return txid.GetString() ?? string.Empty;
    }

    private static void RejectUnknownProperties(JsonElement element, params string[] allowed)
    {
        var allowedSet = allowed.ToHashSet(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (allowedSet.Contains(property.Name)) continue;
            throw new BitcoinGatewayException(BitcoinErrorCodes.InvalidTransaction,
                $"Unknown argument '{property.Name}'.");
        }
    }

    private object[] ToolDefinitions() =>
    [
        new
        {
            name = BitcoinGatewayTools.FeeEstimates,
            description = $"Paid ({_prices.FeeEstimates.PriceSats} sats): query LiveAuth's Bitcoin node for normalized 1, 3, 6, 25, and 144-block fee-rate estimates. Estimates do not guarantee confirmation.",
            inputSchema = EmptySchema(),
            annotations = new { readOnlyHint = true, destructiveHint = false }
        },
        new
        {
            name = BitcoinGatewayTools.MempoolSummary,
            description = $"Paid ({_prices.MempoolSummary.PriceSats} sats): inspect a compact, cached summary of LiveAuth's Bitcoin node mempool. Does not return the full mempool.",
            inputSchema = EmptySchema(),
            annotations = new { readOnlyHint = true, destructiveHint = false }
        },
        new
        {
            name = BitcoinGatewayTools.PreflightTransaction,
            description = $"Paid ({_prices.PreflightTransaction.PriceSats} sats): validate signed raw Bitcoin transaction hex with testmempoolaccept and return a signed observation receipt. This tool NEVER broadcasts or alters the transaction.",
            inputSchema = RawTransactionSchema(),
            annotations = new { readOnlyHint = true, destructiveHint = false }
        },
        new
        {
            name = BitcoinGatewayTools.BroadcastTransaction,
            description = $"Paid on success ({_prices.BroadcastTransaction.PriceSats} sats): preflight and, only if accepted by node and LiveAuth safety policy, submit signed raw Bitcoin transaction hex. This tool CAN broadcast the transaction to the Bitcoin network. Use an idempotency key for safe retries.",
            inputSchema = RawTransactionSchema(),
            annotations = new { readOnlyHint = false, destructiveHint = true }
        },
        new
        {
            name = BitcoinGatewayTools.TransactionStatus,
            description = $"Paid ({_prices.TransactionStatus.PriceSats} sats): observe a transaction as mempool, confirmed, or not_found and receive a signed observation receipt.",
            inputSchema = new
            {
                type = "object",
                properties = new { txid = new { type = "string", pattern = "^[0-9a-fA-F]{64}$", description = "64-character transaction ID" } },
                required = new[] { "txid" },
                additionalProperties = false
            },
            annotations = new { readOnlyHint = true, destructiveHint = false }
        }
    ];

    private static object EmptySchema() => new { type = "object", properties = new { }, additionalProperties = false };
    private static object RawTransactionSchema() => new
    {
        type = "object",
        properties = new { rawTransaction = new { type = "string", pattern = "^(?:[0-9a-fA-F]{2})+$", maxLength = 8_000_000, description = "Fully signed raw Bitcoin transaction hex. Never provide private keys or seed phrases." } },
        required = new[] { "rawTransaction" },
        additionalProperties = false
    };
    private static JsonElement EmptyObject() => JsonDocument.Parse("{}").RootElement.Clone();
    private IActionResult RpcResult(JsonElement? id, object result) => Ok(new { jsonrpc = "2.0", id, result });
    private IActionResult RpcError(JsonElement? id, int code, string message, object? data = null)
        => Ok(new { jsonrpc = "2.0", id, error = new { code, message, data } });
}

public sealed class BitcoinMcpJsonRpcRequest
{
    public string Jsonrpc { get; set; } = string.Empty;
    public JsonElement? Id { get; set; }
    public string Method { get; set; } = string.Empty;
    public JsonElement? Params { get; set; }
}
