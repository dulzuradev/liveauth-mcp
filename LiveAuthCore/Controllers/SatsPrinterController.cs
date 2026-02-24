using Microsoft.AspNetCore.Mvc;
using LiveAuthCore.Services;
using Microsoft.AspNetCore.Authorization;

namespace LiveAuthCore.Controllers;

[ApiController]
[Route("api/[controller]")]
[Route("api/sats")]  // Support both casing
public class SatsPrinterController : ControllerBase
{
    private readonly SatsPrinterService _satsPrinterService;
    private readonly AgentSatsService _agentSatsService;
    private readonly ILogger<SatsPrinterController> _logger;

    public SatsPrinterController(
        SatsPrinterService satsPrinterService,
        AgentSatsService agentSatsService,
        ILogger<SatsPrinterController> logger)
    {
        _satsPrinterService = satsPrinterService;
        _agentSatsService = agentSatsService;
        _logger = logger;
    }

    // ============================================================
    // LND-based Sats Printing (Primary Revenue Path)
    // ============================================================

    /// <summary>
    /// Create a Lightning invoice for adding sats to agent's balance
    /// Human pays this invoice, then agent receives sats
    /// </summary>
    [HttpPost("invoice")]
    public async Task<IActionResult> CreateInvoice([FromBody] CreateInvoiceRequest request)
    {
        if (request.Amount <= 0)
            return BadRequest("Amount must be positive.");

        // Get agent ID from API key
        var agentId = GetAgentId();
        if (string.IsNullOrEmpty(agentId))
            return Unauthorized("Agent ID not found.");

        try
        {
            var invoice = await _agentSatsService.CreateInvoiceAsync(agentId, request.Amount);
            
            return Ok(new
            {
                id = invoice.Id,
                amountSats = invoice.AmountSats,
                invoice = invoice.PaymentRequest,
                paymentHash = invoice.PaymentHash,
                status = invoice.Status,
                expiresAt = invoice.ExpiresAt,
                message = "Pay this invoice to add sats to your agent's balance"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating invoice for agent {AgentId}", agentId);
            return StatusCode(500, $"Error creating invoice: {ex.Message}");
        }
    }

    /// <summary>
    /// Get agent's sats balance
    /// </summary>
    [HttpGet("balance")]
    public async Task<IActionResult> GetBalance()
    {
        var agentId = GetAgentId();
        if (string.IsNullOrEmpty(agentId))
            return Unauthorized("Agent ID not found.");

        try
        {
            var balance = await _agentSatsService.GetBalanceAsync(agentId);
            
            return Ok(new
            {
                agentId = balance.AgentId,
                balance = balance.Balance,
                totalEarned = balance.TotalEarned,
                totalSpent = balance.TotalSpent,
                lastUpdated = balance.LastUpdated
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting balance for agent {AgentId}", agentId);
            return StatusCode(500, $"Error getting balance: {ex.Message}");
        }
    }

    /// <summary>
    /// Check invoice payment status and credit if paid
    /// </summary>
    [HttpPost("check")]
    public async Task<IActionResult> CheckPayment([FromBody] CheckPaymentRequest request)
    {
        if (string.IsNullOrEmpty(request.PaymentHash))
            return BadRequest("Payment hash is required.");

        var agentId = GetAgentId();
        
        try
        {
            var isPaid = await _agentSatsService.CheckAndCreditInvoiceAsync(request.PaymentHash);
            
            return Ok(new
            {
                paymentHash = request.PaymentHash,
                paid = isPaid,
                message = isPaid ? "Payment confirmed, sats credited!" : "Payment not yet received"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking payment {PaymentHash}", request.PaymentHash);
            return StatusCode(500, $"Error checking payment: {ex.Message}");
        }
    }

    /// <summary>
    /// Get invoice history
    /// </summary>
    [HttpGet("invoices")]
    public async Task<IActionResult> GetInvoices()
    {
        var agentId = GetAgentId();
        if (string.IsNullOrEmpty(agentId))
            return Unauthorized("Agent ID not found.");

        try
        {
            var invoices = await _agentSatsService.GetInvoicesAsync(agentId);
            
            return Ok(invoices.Select(i => new
            {
                id = i.Id,
                amountSats = i.AmountSats,
                paymentHash = i.PaymentHash,
                status = i.Status,
                createdAt = i.CreatedAt,
                paidAt = i.PaidAt,
                expiresAt = i.ExpiresAt
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting invoices for agent {AgentId}", agentId);
            return StatusCode(500, $"Error getting invoices: {ex.Message}");
        }
    }

    // ============================================================
    // Demo Mode (for testing without real payments)
    // ============================================================

    /// <summary>
    /// Demo: Print sats to a Lightning address (for testing/demos)
    /// </summary>
    [HttpPost("demo/print")]
    public async Task<IActionResult> DemoPrintSats([FromBody] DemoPrintRequest request)
    {
        if (request.Amount <= 0)
            return BadRequest("Amount must be positive.");

        try
        {
            var invoiceId = Guid.NewGuid().ToString("N")[..16];
            var memo = $"LiveAuth Sats Printer Demo - {request.Amount} sats";
            
            // Fake invoice for demo
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
                message = "Demo mode: Click 'Simulate Payment' to complete."
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
    public async Task<IActionResult> DemoConfirmPayment([FromBody] ConfirmDemoRequest request)
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

    // ============================================================
    // Cashu (NUT-04) - Legacy / Optional
    // ============================================================

    /// <summary>
    /// NUT-04: Mint ecash by paying a Lightning invoice
    /// </summary>
    [HttpPost("print")]
    public async Task<IActionResult> PrintSats([FromBody] CashuPrintRequest request)
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
    /// NUT-05: Melt ecash to pay a Lightning invoice
    /// </summary>
    [HttpPost("melt")]
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

    // ============================================================
    // Helpers
    // ============================================================

    private string? GetAgentId()
    {
        // Try to get from API key header (set by middleware)
        if (HttpContext.Items.TryGetValue("LW_Project", out var project) && project is Data.Entities.Project proj)
        {
            return proj.Id.ToString();
        }
        
        // Fallback to JWT claims
        return User.FindFirst("userId")?.Value ?? User.FindFirst("sub")?.Value;
    }
}

// Request/Response DTOs
public class CreateInvoiceRequest
{
    public long Amount { get; set; }
}

public class CheckPaymentRequest
{
    public string PaymentHash { get; set; } = string.Empty;
}

public class DemoPrintRequest
{
    public long Amount { get; set; }
    public string? LightningAddress { get; set; }
}

public class ConfirmDemoRequest
{
    public string InvoiceId { get; set; } = string.Empty;
}

public class CashuPrintRequest
{
    public long Amount { get; set; }
    public string? MintUrl { get; set; }
}

public class MeltSatsRequest
{
    public string Invoice { get; set; } = string.Empty;
    public string? MintUrl { get; set; }
}
