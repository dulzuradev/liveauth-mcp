using LiveAuthCore.Services;
using Microsoft.AspNetCore.Mvc;

namespace LiveAuthCore.Controllers;

[ApiController]
[Route("api/public/l402")]
public class L402Controller : ControllerBase
{
    private readonly L402Service _l402;
    private readonly LightningService _lightning;

    public L402Controller(L402Service l402, LightningService lightning)
    {
        _l402 = l402;
        _lightning = lightning;
    }

    /// <summary>
    /// Create an L402 invoice for API access.
    /// </summary>
    /// <param name="destination">Optional destination/identifier</param>
    /// <param name="amountSats">Satoshis per request (optional, defaults to config)</param>
    [HttpPost("invoice")]
    public async Task<IActionResult> CreateInvoice(
        [FromQuery] string? destination = null,
        [FromQuery] int? amountSats = null)
    {
        try
        {
            var response = await _l402.CreateInvoiceAsync(destination, amountSats);
            return Ok(new
            {
                paymentHash = response.PaymentHash,
                bolt11 = response.Bolt11,
                amountSats = response.AmountSats,
                expiresAtUnix = response.ExpiresAtUnix,
                // Hint: client pays invoice, then calls /validate with preimage
                instructions = "Pay this invoice, then call /validate with your preimage (payment secret) to get an L402 token"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Validate payment and get L402 token.
    /// </summary>
    /// <param name="paymentHash">The payment hash (r_hash) from the invoice</param>
    [HttpPost("validate")]
    public async Task<IActionResult> ValidatePayment([FromQuery] string paymentHash)
    {
        if (string.IsNullOrEmpty(paymentHash))
            return BadRequest(new { error = "paymentHash is required" });

        try
        {
            // Check if invoice is paid
            var status = await _lightning.GetInvoiceStatusAsync(paymentHash);
            
            if (!status.IsPaid)
            {
                return StatusCode(402, new 
                { 
                    error = "Payment required",
                    message = "Invoice has not been paid yet"
                });
            }

            // Invoice is paid - issue token
            var token = await _l402.IssueTokenAsync(paymentHash);
            
            if (string.IsNullOrEmpty(token))
            {
                return StatusCode(500, new { error = "Failed to issue token" });
            }

            return Ok(new
            {
                token = token,
                tokenType = "L402",
                expiresInSeconds = 3600 // 1 hour
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Check if a token is valid (for debugging/health).
    /// </summary>
    [HttpGet("verify")]
    public IActionResult VerifyToken([FromQuery] string token)
    {
        if (string.IsNullOrEmpty(token))
            return BadRequest(new { error = "token is required" });

        var isValid = _l402.IsTokenValid(token);
        
        return Ok(new
        {
            valid = isValid,
            tokenType = "L402"
        });
    }
}
