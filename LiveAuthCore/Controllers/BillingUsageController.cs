using System.Security.Claims;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Data.Entities.Mcp;
using LiveAuthCore.Models.Mcp;
using LiveAuthCore.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JwtRegisteredClaimNames = System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames;

namespace LiveAuthCore.Controllers;

[ApiController]
[Route("api/billing")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class BillingUsageController : ControllerBase
{
    private readonly LiveAuthDbContext _db;
    private readonly LightningService _lightning;

    public BillingUsageController(LiveAuthDbContext db, LightningService lightning)
    {
        _db = db;
        _lightning = lightning;
    }

    private Guid GetDeveloperId()
    {
        var raw =
            User.FindFirst("userId")?.Value ??
            User.FindFirst("developer_id")?.Value ??
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
            User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (!Guid.TryParse(raw, out var id))
            throw new UnauthorizedAccessException("Invalid developer identity");

        return id;
    }

    [HttpGet("usage")]
    public async Task<ActionResult<BillingUsageResponse>> GetUsage(CancellationToken ct)
    {
        var devId = GetDeveloperId();

        var project = await _db.Projects
            .Where(p => p.DeveloperId == devId && p.IsActive && !p.IsDeleted)
            .FirstOrDefaultAsync(ct);

        if (project == null)
            return NotFound("No active project found");

        // Get MCP call stats from today
        var today = DateTime.UtcNow.Date;
        var mcpSessions = await _db.McpGateTokens
            .Where(t => t.ProjectId == project.Id && t.Status == "active")
            .ToListAsync(ct);

        // Aggregate across all active sessions
        long totalCallsToday = 0;
        long totalSatsToday = 0;
        foreach (var s in mcpSessions)
        {
            if (s.DayWindowStart.Date == today)
            {
                totalCallsToday += s.CallsUsed;
                totalSatsToday += s.SatsUsed;
            }
            else
            {
                // Reset stale session
                s.DayWindowStart = today;
                s.CallsUsed = 0;
                s.SatsUsed = 0;
            }
        }

        if (mcpSessions.Any())
            await _db.SaveChangesAsync(ct);

        return Ok(new BillingUsageResponse(
            L402BalanceSats: project.L402BalanceSats,
            CallsUsedToday: totalCallsToday,
            SatsUsedToday: totalSatsToday,
            FreeDailyLimitSats: 10_000, // hardcoded free tier
            FreeDailyLimitCalls: 60
        ));
    }

    /// <summary>
    /// POST /api/billing/purchase
    /// Creates a Lightning invoice to purchase L402 credits.
    /// </summary>
    [HttpPost("purchase")]
    public async Task<ActionResult<PurchaseResponse>> Purchase(
        [FromBody] PurchaseRequest req,
        CancellationToken ct)
    {
        if (req.AmountSats < 10)
            return BadRequest("Minimum purchase is 10 sats");
        if (req.AmountSats > 100_000)
            return BadRequest("Maximum purchase is 100,000 sats at a time");

        var devId = GetDeveloperId();
        var isAdmin = User.IsInRole("Admin");

        // Resolve project
        Project? project;
        if (req.ProjectId.HasValue)
        {
            project = await _db.Projects
                .Where(p => p.Id == req.ProjectId.Value && p.IsActive && !p.IsDeleted)
                .FirstOrDefaultAsync(ct);
            if (project == null)
                return NotFound("Project not found");
            if (!isAdmin && project.DeveloperId != devId)
                return Forbid("Not your project");
        }
        else
        {
            project = await _db.Projects
                .Where(p => p.DeveloperId == devId && p.IsActive && !p.IsDeleted)
                .FirstOrDefaultAsync(ct);
            if (project == null)
                return NotFound("No active project found");
        }

        // Create Lightning invoice
        var invoice = await _lightning.CreateLoginInvoiceAsync(
            email: $"l402-purchase-{devId:N}",
            amountSats: req.AmountSats,
            expiryMinutes: 30,
            project: project
        );

        // Store purchase intent
        var purchase = new L402Purchase
        {
            ProjectId = project.Id,
            DeveloperId = devId,
            AmountSats = req.AmountSats,
            InvoiceId = invoice.InvoiceId, // hex r_hash
            Bolt11 = invoice.Bolt11,
            ExpiresAtUnix = invoice.ExpiresAtUnix,
            Status = "pending"
        };
        _db.L402Purchases.Add(purchase);
        await _db.SaveChangesAsync(ct);

        return Ok(new PurchaseResponse(
            PurchaseId: purchase.Id,
            Bolt11: invoice.Bolt11,
            AmountSats: req.AmountSats,
            ExpiresAtUnix: invoice.ExpiresAtUnix,
            Status: "pending"
        ));
    }

    /// <summary>
    /// GET /api/billing/purchase/{purchaseId}
    /// Checks invoice payment status and auto-credits balance on settlement.
    /// </summary>
    [HttpGet("purchase/{purchaseId:guid}")]
    public async Task<ActionResult<PurchaseStatusResponse>> GetPurchaseStatus(
        Guid purchaseId,
        CancellationToken ct)
    {
        var purchase = await _db.L402Purchases
            .Where(p => p.Id == purchaseId)
            .FirstOrDefaultAsync(ct);

        if (purchase == null)
            return NotFound("Purchase not found");

        // Auth check: only the owner or admin can check status
        var devId = GetDeveloperId();
        var isAdmin = User.IsInRole("Admin");
        if (!isAdmin && purchase.DeveloperId != devId)
            return Forbid("Not your purchase");

        // If already settled, return immediately
        if (purchase.Status == "settled")
        {
            var proj = await _db.Projects.Where(p => p.Id == purchase.ProjectId).FirstOrDefaultAsync(ct);
            return Ok(new PurchaseStatusResponse(
                PurchaseId: purchase.Id,
                Status: "settled",
                AmountSats: purchase.AmountSats,
                NewBalanceSats: proj?.L402BalanceSats,
                Bolt11: purchase.Bolt11
            ));
        }

        // If expired, mark it
        if (purchase.Status == "pending" && purchase.ExpiresAtUnix < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            purchase.Status = "expired";
            await _db.SaveChangesAsync(ct);
            return Ok(new PurchaseStatusResponse(
                PurchaseId: purchase.Id,
                Status: "expired",
                AmountSats: purchase.AmountSats,
                NewBalanceSats: null,
                Bolt11: purchase.Bolt11
            ));
        }

        // Poll LND for settlement
        var invoiceStatus = await _lightning.GetInvoiceStatusAsync(purchase.InvoiceId);

        if (invoiceStatus.IsPaid && purchase.Status == "pending")
        {
            // Mark as settling, credit balance, mark settled
            purchase.Status = "settling";

            var project = await _db.Projects
                .Where(p => p.Id == purchase.ProjectId)
                .FirstOrDefaultAsync(ct);

            if (project != null)
            {
                project.L402BalanceSats += purchase.AmountSats;
                purchase.Status = "settled";
                purchase.SettledAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(ct);

            return Ok(new PurchaseStatusResponse(
                PurchaseId: purchase.Id,
                Status: "settled",
                AmountSats: purchase.AmountSats,
                NewBalanceSats: project?.L402BalanceSats,
                Bolt11: purchase.Bolt11
            ));
        }

        // Still pending
        return Ok(new PurchaseStatusResponse(
            PurchaseId: purchase.Id,
            Status: purchase.Status,
            AmountSats: purchase.AmountSats,
            NewBalanceSats: null,
            Bolt11: purchase.Bolt11
        ));
    }

    /// <summary>
    /// Dev-only: add sats to a project's L402 balance (admin/topup).
    /// POST /api/billing/topup
    /// </summary>
    [HttpPost("topup")]
    public async Task<ActionResult<TopupResponse>> TopUp([FromBody] TopupRequest req, CancellationToken ct)
    {
        if (req.AmountSats <= 0)
            return BadRequest("Amount must be positive");

        if (req.AmountSats > 1_000_000)
            return BadRequest("Max topup is 1,000,000 sats at a time");

        // Admin can top up any project
        var isAdmin = User.IsInRole("Admin");

        Project? project = null;

        if (req.ProjectId.HasValue)
        {
            project = await _db.Projects
                .Where(p => p.Id == req.ProjectId.Value && p.IsActive && !p.IsDeleted)
                .FirstOrDefaultAsync(ct);

            if (project == null)
                return NotFound("Project not found");

            // Non-admin: can only top up own project
            if (!isAdmin)
            {
                var devId = GetDeveloperId();
                if (project.DeveloperId != devId)
                    return Forbid("Not your project");
            }
        }
        else
        {
            // No projectId: use developer's default project
            var devId = isAdmin ? (Guid?)null : GetDeveloperId();
            var query = _db.Projects.Where(p => p.IsActive && !p.IsDeleted);
            project = devId.HasValue
                ? await query.Where(p => p.DeveloperId == devId.Value).FirstOrDefaultAsync(ct)
                : await query.FirstOrDefaultAsync(ct);

            if (project == null)
                return NotFound("No active project found");
        }

        project.L402BalanceSats += req.AmountSats;
        await _db.SaveChangesAsync(ct);

        return Ok(new TopupResponse(
            ProjectId: project.Id,
            AmountAdded: req.AmountSats,
            NewBalance: project.L402BalanceSats
        ));
    }
}
