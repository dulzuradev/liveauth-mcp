using LiveAuthCore.Bitcoin.Models;
using LiveAuthCore.Bitcoin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LiveAuthCore.Bitcoin.Controllers;

[ApiController]
[Route("api/bitcoin")]
[Authorize(Roles = "McpClient")]
[EnableRateLimiting("bitcoin-gateway")]
[RequestSizeLimit(8_100_000)] // hard ceiling for the 4 MB raw-byte safety maximum encoded as hex/JSON
public sealed class BitcoinController : ControllerBase
{
    private readonly IBitcoinGatewayExecutionService _gateway;
    private readonly ILogger<BitcoinController> _logger;

    public BitcoinController(IBitcoinGatewayExecutionService gateway, ILogger<BitcoinController> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    [HttpGet("fees")]
    public Task<IActionResult> Fees(CancellationToken ct)
        => ExecuteAsync(() => _gateway.GetFeeEstimatesAsync(User, IdempotencyKey(),
            HttpContext.TraceIdentifier, ct));

    [HttpGet("mempool")]
    public Task<IActionResult> Mempool(CancellationToken ct)
        => ExecuteAsync(() => _gateway.GetMempoolSummaryAsync(User, IdempotencyKey(),
            HttpContext.TraceIdentifier, ct));

    [HttpPost("transactions/preflight")]
    public Task<IActionResult> Preflight([FromBody] BitcoinRawTransactionRequest request, CancellationToken ct)
        => ExecuteAsync(() => _gateway.PreflightAsync(User, request.RawTransaction, IdempotencyKey(),
            HttpContext.TraceIdentifier, ct));

    [HttpPost("transactions/broadcast")]
    public Task<IActionResult> Broadcast([FromBody] BitcoinRawTransactionRequest request, CancellationToken ct)
        => ExecuteAsync(() => _gateway.BroadcastAsync(User, request.RawTransaction, IdempotencyKey(),
            HttpContext.TraceIdentifier, ct));

    [HttpGet("transactions/{txid}")]
    public Task<IActionResult> Status(string txid, CancellationToken ct)
        => ExecuteAsync(() => _gateway.GetTransactionStatusAsync(User, txid, IdempotencyKey(),
            HttpContext.TraceIdentifier, ct));

    private async Task<IActionResult> ExecuteAsync<T>(Func<Task<BitcoinPaidResult<T>>> action)
    {
        try
        {
            var result = await action();
            Response.Headers["X-LiveAuth-Price-Sats"] = result.PriceSats.ToString();
            Response.Headers["X-LiveAuth-Idempotent-Replay"] = result.Duplicate ? "true" : "false";
            return Ok(result.Value);
        }
        catch (BitcoinGatewayException ex)
        {
            if (ex.RetryAfterSeconds.HasValue)
                Response.Headers.RetryAfter = ex.RetryAfterSeconds.Value.ToString();
            return StatusCode(ex.StatusCode, new BitcoinErrorEnvelope(new BitcoinError(
                ex.Code, ex.Message, ex.Retryable, HttpContext.TraceIdentifier, ex.RetryAfterSeconds)));
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bitcoin Gateway HTTP request {RequestId} failed.", HttpContext.TraceIdentifier);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new BitcoinErrorEnvelope(new BitcoinError("LIVEAUTH_BITCOIN_INTERNAL_ERROR",
                    "LiveAuth could not complete the Bitcoin operation. The call was not charged.",
                    true, HttpContext.TraceIdentifier)));
        }
    }

    private string? IdempotencyKey()
        => Request.Headers["X-LiveAuth-Idempotency-Key"].FirstOrDefault();
}
