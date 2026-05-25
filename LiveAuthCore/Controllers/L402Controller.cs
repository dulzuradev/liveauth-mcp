using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Models;
using LiveAuthCore.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Controllers;

[ApiController]
[Route("api/public/l402")]
public class L402Controller : ControllerBase
{
    private const string PublicKeyHeaderName = "X-LW-Public";
    private readonly L402Service _l402;
    private readonly LightningService _lightning;
    private readonly LiveAuthDbContext _db;

    public L402Controller(L402Service l402, LightningService lightning, LiveAuthDbContext db)
    {
        _l402 = l402;
        _lightning = lightning;
        _db = db;
    }

    /// <summary>
    /// Create an L402 invoice for API access.
    /// </summary>
    /// <param name="destination">Optional destination/identifier</param>
    /// <param name="amountSats">Satoshis per request (optional, defaults to config)</param>
    [HttpPost("invoice")]
    public async Task<IActionResult> CreateInvoice(
        [FromQuery] string? destination = null,
        [FromQuery] int? amountSats = null,
        [FromQuery] string? publicKey = null,
        CancellationToken ct = default)
    {
        try
        {
            var project = await ResolveActiveProjectAsync(publicKey, ct);
            if (project == null)
                return Unauthorized(new { error = "invalid_public_key", message = "A valid active project public key is required." });

            var response = await _l402.CreateInvoiceAsync(destination, amountSats, project);
            _l402.BindInvoiceToProject(response.PaymentHash, project.Id);

            return Ok(new
            {
                paymentHash = response.PaymentHash,
                bolt11 = response.Bolt11,
                amountSats = response.AmountSats,
                expiresAtUnix = response.ExpiresAtUnix,
                tokenScope = "time_scoped_bearer",
                tokenScopeDescription = "Validated payments issue an L402 bearer token bound to this project and valid for the configured token TTL. Current v0.1 tokens are time-scoped, not single-use.",
                instructions = "Pay this invoice, then call /validate with the paymentHash to get an L402 token."
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
    public async Task<IActionResult> ValidatePayment(
        [FromQuery] string paymentHash,
        [FromQuery] string? publicKey = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(paymentHash))
            return BadRequest(new { error = "paymentHash is required" });

        try
        {
            var project = await ResolveActiveProjectAsync(publicKey, ct);
            if (project == null)
                return Unauthorized(new { error = "invalid_public_key", message = "A valid active project public key is required." });

            if (!_l402.IsInvoiceBoundToProject(paymentHash, project.Id))
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "invoice_project_mismatch" });

            // Check if invoice is paid
            var status = await _lightning.GetInvoiceStatusAsync(paymentHash, project);
            
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
                expiresInSeconds = 3600, // 1 hour
                tokenScope = "time_scoped_bearer",
                tokenScopeDescription = "This L402 token is bound to the project public key used for invoice creation and remains valid for the configured token TTL. Current v0.1 tokens are time-scoped, not single-use."
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
    public async Task<IActionResult> VerifyToken(
        [FromQuery] string token,
        [FromQuery] string? publicKey = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(token))
            return BadRequest(new { error = "token is required" });

        var project = await ResolveActiveProjectAsync(publicKey, ct);
        if (project == null)
            return Unauthorized(new { error = "invalid_public_key", message = "A valid active project public key is required." });

        var isValid = _l402.IsTokenValid(token, project.Id);
        
        return Ok(new
        {
            valid = isValid,
            tokenType = "L402"
        });
    }

    // ──────────────────────────────────────────────────────────────────
    // Bundle purchase flow
    // POST /api/public/l402/bundle/invoice  — create bundle purchase invoice
    // POST /api/public/l402/bundle/claim    — poll for macaroon after payment
    // GET  /api/public/l402/bundle/status   — check bundle balance/status
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Create a Lightning invoice for a bundle purchase.
    /// </summary>
    [HttpPost("bundle/invoice")]
    public async Task<IActionResult> CreateBundleInvoice(
        [FromBody] CreateBundleInvoiceRequest req,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Tier))
            return BadRequest(new { error = "Tier is required (starter, growth, scale, enterprise)" });

        var project = await ResolveActiveProjectAsync(req.PublicKey, ct);
        if (project == null)
            return Unauthorized(new { error = "invalid_public_key", message = "A valid active project public key is required." });

        if (!L402BundleTiers.TryGetTier(req.Tier, out var tier))
            return BadRequest(new { error = $"Unknown tier '{req.Tier}'. Valid: starter, growth, scale, enterprise" });

        var bundleId = $"bundle_{req.Tier}_{Guid.NewGuid().ToString("N")[..12]}";
        var memo = $"LiveAuth {tier.Name} bundle — {tier.TotalCalls} calls";

        var result = await _lightning.CreateLoginInvoiceAsync(
            email: req.AgentId ?? "bundle-purchase",
            amountSats: tier.PriceSats,
            expiryMinutes: 10,
            project: project
        );

        var bundle = new L402Bundle
        {
            Id = Guid.NewGuid(),
            BundleId = bundleId,
            ProjectId = project.Id,
            DeveloperId = project.DeveloperId,
            Tier = tier.Name,
            TotalCalls = tier.TotalCalls,
            RemainingCalls = tier.TotalCalls,
            AmountSats = tier.PriceSats,
            ExpiresAtUnix = result.ExpiresAtUnix,
            PaymentHash = result.InvoiceId,
            Bolt11 = result.Bolt11,
            Status = "pending",
            AgentId = req.AgentId
        };

        _db.L402Bundles.Add(bundle);
        await _db.SaveChangesAsync(ct);

        return Ok(new CreateBundleInvoiceResponse
        {
            BundleId = bundleId,
            Invoice = result.Bolt11,
            Bolt11 = result.Bolt11,
            PaymentHash = result.InvoiceId,
            AmountSats = tier.PriceSats,
            ExpiresAtUnix = result.ExpiresAtUnix,
            Tier = tier.Name,
            TotalCalls = tier.TotalCalls
        });
    }

    /// <summary>
    /// Poll for bundle activation after payment.
    /// Pass the payment hash from the invoice response.
    /// </summary>
    [HttpPost("bundle/claim")]
    public async Task<IActionResult> ClaimBundle(
        [FromBody] ClaimBundleRequest req,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.PaymentHash))
            return BadRequest(new { error = "paymentHash is required" });

        var project = await ResolveActiveProjectAsync(req.PublicKey, ct);
        if (project == null)
            return Unauthorized(new { error = "invalid_public_key", message = "A valid active project public key is required." });

        var bundle = await _db.L402Bundles
            .FirstOrDefaultAsync(b => b.PaymentHash == req.PaymentHash, ct);

        if (bundle == null)
            return NotFound(new { error = "Bundle not found for this payment hash" });

        if (bundle.ProjectId != project.Id)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "bundle_project_mismatch" });

        if (bundle.Status == "pending")
        {
            // Check if invoice is paid
            var status = await _lightning.GetInvoiceStatusAsync(req.PaymentHash, project);
            if (!status.IsPaid)
            {
                return StatusCode(402, new
                {
                    error = "Payment not yet received",
                    message = "Invoice has not been paid"
                });
            }

            // Activate the bundle
            bundle.Status = "active";
            bundle.ExpiresAtUnix = DateTimeOffset.UtcNow.AddDays(L402BundleTiers.DefaultValidityDays)
                .ToUnixTimeSeconds();
            await _db.SaveChangesAsync(ct);
        }

        if (bundle.Status == "active" && bundle.RemainingCalls <= 0)
            bundle.Status = "depleted";

        // Issue macaroon
        var (macaroon, signature) = _l402.IssueMacaroonForBundle(bundle);

        await _db.SaveChangesAsync(ct);

        return Ok(new ClaimBundleResponse
        {
            Macaroon = macaroon,
            BundleId = bundle.BundleId,
            RemainingCalls = bundle.RemainingCalls,
            ExpiresAtUnix = bundle.ExpiresAtUnix,
            Scopes = new[] { "mcp.verify", "auth.start" }
        });
    }

    /// <summary>
    /// Check bundle status — remaining calls, expiry, etc.
    /// </summary>
    [HttpGet("bundle/status")]
    public async Task<IActionResult> GetBundleStatus(
        [FromQuery] string? bundleId,
        [FromQuery] string? paymentHash,
        [FromQuery] string? publicKey,
        CancellationToken ct)
    {
        var project = await ResolveActiveProjectAsync(publicKey, ct);
        if (project == null)
            return Unauthorized(new { error = "invalid_public_key", message = "A valid active project public key is required." });

        L402Bundle? bundle = null;

        if (!string.IsNullOrWhiteSpace(bundleId))
            bundle = await _db.L402Bundles.FirstOrDefaultAsync(b => b.BundleId == bundleId, ct);
        else if (!string.IsNullOrWhiteSpace(paymentHash))
            bundle = await _db.L402Bundles.FirstOrDefaultAsync(b => b.PaymentHash == paymentHash, ct);

        if (bundle == null)
            return NotFound(new { error = "Bundle not found" });

        if (bundle.ProjectId != project.Id)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "bundle_project_mismatch" });

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var isExpired = bundle.ExpiresAtUnix > 0 && bundle.ExpiresAtUnix < now;
        var isDepleted = bundle.RemainingCalls <= 0;

        return Ok(new BundleStatusResponse
        {
            BundleId = bundle.BundleId,
            Tier = bundle.Tier,
            TotalCalls = bundle.TotalCalls,
            RemainingCalls = bundle.RemainingCalls,
            UsedCalls = bundle.TotalCalls - bundle.RemainingCalls,
            ExpiresAtUnix = bundle.ExpiresAtUnix,
            IsExpired = isExpired,
            IsDepleted = isDepleted
        });
    }

    private async Task<Project?> ResolveActiveProjectAsync(string? publicKey, CancellationToken ct)
    {
        var key = publicKey;
        if (string.IsNullOrWhiteSpace(key) &&
            Request.Headers.TryGetValue(PublicKeyHeaderName, out var headerValues))
        {
            key = headerValues.FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(key))
            return null;

        key = key.Trim();

        var project = await _db.Projects
            .FirstOrDefaultAsync(p => p.PublicKey == key && p.IsActive, ct);
        if (project != null)
            return project;

        var apiKey = await _db.ProjectApiKeys
            .Include(k => k.Project)
            .FirstOrDefaultAsync(k => k.PublicKey == key && k.IsActive && k.Project.IsActive, ct);

        return apiKey?.Project;
    }
}
