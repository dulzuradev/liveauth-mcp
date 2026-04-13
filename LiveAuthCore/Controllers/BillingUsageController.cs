using System.Security.Claims;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Data.Entities.Mcp;
using LiveAuthCore.Models.Mcp;
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

    public BillingUsageController(LiveAuthDbContext db)
    {
        _db = db;
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
    /// Dev-only: add sats to a project's L402 balance.
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