using System.Security.Claims;
using LiveAuthCore.Models.CostShield;
using LiveAuthCore.Services.CostShield;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using JwtRegisteredClaimNames = System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames;

namespace LiveAuthCore.Controllers;

[ApiController]
[Route("api/dev/projects/{projectId:guid}/costshield")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class DeveloperCostShieldAnalyticsController : ControllerBase
{
    private readonly ICostShieldAnalyticsService _analytics;

    public DeveloperCostShieldAnalyticsController(
        ICostShieldAnalyticsService analytics)
    {
        _analytics = analytics;
    }

    [HttpGet("overview")]
    public async Task<ActionResult<CostShieldOverviewResponse>> GetOverview(
        Guid projectId,
        [FromQuery] int windowHours = 24,
        CancellationToken ct = default)
    {
        var result = await _analytics.GetOverviewAsync(
            projectId,
            GetDeveloperId(),
            User.IsInRole("Admin"),
            windowHours,
            ct);

        return result.Status switch
        {
            CostShieldAnalyticsStatus.Found => Ok(result.Value),
            CostShieldAnalyticsStatus.Invalid => BadRequest(new
            {
                error = "invalid_window",
                message = "windowHours must be between 1 and 720."
            }),
            _ => NotFound()
        };
    }

    [HttpGet("events")]
    public async Task<ActionResult<CostShieldEventListResponse>> GetEvents(
        Guid projectId,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        var result = await _analytics.GetEventsAsync(
            projectId,
            GetDeveloperId(),
            User.IsInRole("Admin"),
            limit,
            offset,
            ct);

        return result.Status switch
        {
            CostShieldAnalyticsStatus.Found => Ok(result.Value),
            CostShieldAnalyticsStatus.Invalid => BadRequest(new
            {
                error = "invalid_paging",
                message = "limit must be 1-100 and offset must be 0-100000."
            }),
            _ => NotFound()
        };
    }

    private Guid GetDeveloperId()
    {
        var raw =
            User.FindFirst("userId")?.Value ??
            User.FindFirst("developer_id")?.Value ??
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
            User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (!Guid.TryParse(raw, out var developerId))
            throw new UnauthorizedAccessException("Invalid developer identity");

        return developerId;
    }
}
