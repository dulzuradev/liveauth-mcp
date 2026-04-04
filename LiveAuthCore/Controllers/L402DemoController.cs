using LiveAuthCore.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace LiveAuthCore.Controllers;

/// <summary>
/// Demo-only L402 endpoints — no real Lightning payments.
/// Used by the docs/demo.html page to demonstrate L402 flow
/// without touching production L402 endpoints.
/// </summary>
[ApiController]
[Route("api/public/demo/l402")]
public class L402DemoController : ControllerBase
{
    private readonly LightningService _lightning;
    private readonly IMemoryCache _cache;

    public L402DemoController(LightningService lightning, IMemoryCache cache)
    {
        _lightning = lightning;
        _cache = cache;
    }

    public class DemoInvoiceResponse
    {
        public string PaymentHash { get; set; } = "";
        public string Bolt11 { get; set; } = "";
        public int AmountSats { get; set; }
        public long ExpiresAtUnix { get; set; }
        public string Instructions { get; set; } = "";
        public bool IsDemo { get; set; } = true;
    }

    public class DemoValidateResponse
    {
        public bool Valid { get; set; }
        public string? Token { get; set; }
        public string? Error { get; set; }
        public bool IsDemo { get; set; } = true;
    }

    /// <summary>
    /// Create a demo L402 invoice — always succeeds with a fake Lightning invoice.
    /// </summary>
    [HttpPost("invoice")]
    public IActionResult CreateDemoInvoice([FromQuery] string? destination = null)
    {
        var paymentHash = $"demo_{Guid.NewGuid():N}";
        // Fake bolt11 for demo purposes (not a real Lightning invoice)
        var bolt11 = "lnbc1p0demo1demo1demo1demo1demo1demo1demo1demo1demo1demo1demo1demo1demo1demo1demo1demo1demo1demo1demo1demo0demo";

        return Ok(new DemoInvoiceResponse
        {
            PaymentHash = paymentHash,
            Bolt11 = bolt11,
            AmountSats = 1,
            ExpiresAtUnix = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds(),
            Instructions = "This is a demo invoice. No real payment needed — click Simulate to continue.",
            IsDemo = true
        });
    }

    /// <summary>
    /// Validate a demo L402 payment — always succeeds, no real Lightning check.
    /// </summary>
    [HttpPost("validate")]
    public IActionResult ValidateDemoPayment([FromQuery] string paymentHash)
    {
        if (string.IsNullOrWhiteSpace(paymentHash))
            return BadRequest(new DemoValidateResponse 
            { 
                Valid = false, 
                Error = "paymentHash is required",
                IsDemo = true
            });

        // Generate a demo L402 token
        var token = $"demo_l402_{Guid.NewGuid():N}";

        return Ok(new DemoValidateResponse
        {
            Valid = true,
            Token = token,
            IsDemo = true
        });
    }

    /// <summary>
    /// Verify a demo L402 token — always valid.
    /// </summary>
    [HttpGet("verify")]
    public IActionResult VerifyDemoToken([FromQuery] string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return BadRequest(new { error = "token is required", isDemo = true });

        return Ok(new
        {
            valid = true,
            token = token,
            expiresAtUnix = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
            isDemo = true
        });
    }
}