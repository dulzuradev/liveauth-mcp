using System.Security.Claims;
using LiveAuthCore.Data;
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
}