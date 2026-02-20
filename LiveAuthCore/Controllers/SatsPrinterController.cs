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
    /// Demo: Print sats to a Lightning address (for testing/demos)
    /// Shows a QR code for payment, then simulates confirmation
    /// </summary>
    [HttpPost("demo/print")]
    public async Task<IActionResult> DemoPrintSats([FromBody] PrintSatsRequest request)
    {
        if (request.Amount <= 0)
            return BadRequest("Amount must be positive.");

        try
        {
            // Generate a demo Lightning invoice for the agent's Lightning address
            // In production, this would create a real invoice that pays to the agent's node
            // For demo: generate a mock invoice and simulate the flow
            
            var invoiceId = Guid.NewGuid().ToString("N")[..16];
            var memo = $"LiveAuth Sats Printer Demo - {request.Amount} sats";
            
            // Create a fake but valid-looking invoice for demo
            // Real implementation would use LND or external service
            var demoInvoice = $"lnbc{request.Amount}n1p${invoiceId}test";
            
            _logger.LogInformation("Demo print sats: {Amount} sats to {LightningAddress}", 
                request.Amount, request.LightningAddress);
            
            return Ok(new
            {
                id = invoiceId,
                status = "pending_payment",
                amount = request.Amount,
                lightningAddress = request.LightningAddress,
                invoice = demoInvoice,
                paymentHash = invoiceId,
                demo = true,
                message = "Demo mode: In production, scan QR to pay. Click 'Simulate Payment' to complete."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error printing sats for demo");
            return StatusCode(500, $"Error printing sats: {ex.Message}");
        }
    }

    /// <summary>
    /// Demo: Simulate payment confirmation (for demo/testing only)
    /// </summary>
    [HttpPost("demo/confirm")]
    public async Task<IActionResult> DemoConfirmPayment([FromBody] ConfirmDemoPaymentRequest request)
    {
        if (string.IsNullOrEmpty(request.InvoiceId))
            return BadRequest("InvoiceId is required.");
            
        _logger.LogInformation("Demo confirm payment for invoice: {InvoiceId}", request.InvoiceId);
        
        return Ok(new
        {
            id = request.InvoiceId,
            status = "paid",
            message = "Payment simulated successfully! Sats sent to agent."
        });
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
    public string? LightningAddress { get; set; }
    public string? MintUrl { get; set; }
}

public class ConfirmDemoPaymentRequest
{
    public string InvoiceId { get; set; } = string.Empty;
}

public class MeltSatsRequest
{
    public string Invoice { get; set; } = string.Empty;
    public string? MintUrl { get; set; }
}
