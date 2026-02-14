using Microsoft.AspNetCore.Mvc;
using LiveAuthCore.Services;
using Microsoft.AspNetCore.Authorization;

namespace LiveAuthCore.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SatsPrinterController : ControllerBase
{
    private readonly SatsPrinterService _satsPrinterService;
    private readonly ILogger<SatsPrinterController> _logger;

    public SatsPrinterController(
        SatsPrinterService satsPrinterService,
        ILogger<SatsPrinterController> logger)
    {
        _satsPrinterService = satsPrinterService;
        _logger = logger;
    }

    /// <summary>
    /// NUT-04: Mint ecash by paying a Lightning invoice
    /// </summary>
    [HttpPost("print")]
    [Authorize]
    public async Task<IActionResult> PrintSats([FromBody] PrintSatsRequest request)
    {
        if (request.Amount <= 0)
            return BadRequest("Amount must be positive.");

        var userId = User.FindFirst("userId")?.Value ?? User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized("User ID not found in token.");

        try
        {
            var result = await _satsPrinterService.MintSatsAsync(
                userId, 
                request.Amount, 
                request.MintUrl ?? "https://mint.minibits.cash/Bitcoin");
            
            return Ok(new
            {
                id = result.Id,
                status = result.Status.ToString(),
                amount = result.Amount,
                mintUrl = result.MintUrl,
                invoice = result.Invoice,
                paymentHash = result.PaymentHash
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error printing sats for user {UserId}", userId);
            return StatusCode(500, $"Error printing sats: {ex.Message}");
        }
    }

    /// <summary>
    /// Get user's ecash balance across all mints
    /// </summary>
    [HttpGet("balance")]
    [Authorize]
    public async Task<IActionResult> GetBalance()
    {
        var userId = User.FindFirst("userId")?.Value ?? User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized("User ID not found in token.");

        try
        {
            var balances = await _satsPrinterService.GetUserBalanceAsync(userId);
            var totalBalance = balances.Values.Sum();

            return Ok(new
            {
                userId,
                totalBalance,
                balancesByMint = balances
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting balance for user {UserId}", userId);
            return StatusCode(500, $"Error getting balance: {ex.Message}");
        }
    }

    /// <summary>
    /// NUT-05: Melt ecash to pay a Lightning invoice
    /// </summary>
    [HttpPost("melt")]
    [Authorize]
    public async Task<IActionResult> MeltSats([FromBody] MeltSatsRequest request)
    {
        if (string.IsNullOrEmpty(request.Invoice))
            return BadRequest("Invoice is required.");

        var userId = User.FindFirst("userId")?.Value ?? User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized("User ID not found in token.");

        try
        {
            var result = await _satsPrinterService.MeltSatsAsync(
                userId, 
                request.Invoice,
                request.MintUrl ?? "https://mint.minibits.cash/Bitcoin");

            return Ok(new
            {
                paid = result.Paid,
                paymentPreimage = result.Payment_preimage,
                hasChange = result.Change != null && result.Change.Count > 0
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error melting sats for user {UserId}", userId);
            return StatusCode(500, $"Error melting sats: {ex.Message}");
        }
    }
}

public class PrintSatsRequest
{
    public long Amount { get; set; }
    public string? MintUrl { get; set; }
}

public class MeltSatsRequest
{
    public string Invoice { get; set; } = string.Empty;
    public string? MintUrl { get; set; }
}
