using LiveAuthCore.Auth;
using LiveAuthCore.Models.CostShield;
using LiveAuthCore.Services.CostShield;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LiveAuthCore.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = ApiKeyAuthOptions.SchemeName)]
[Route("api/costshield/authorizations")]
public sealed class CostShieldAuthorizationsController : ControllerBase
{
    private readonly ICostShieldVerificationService _verification;

    public CostShieldAuthorizationsController(ICostShieldVerificationService verification)
    {
        _verification = verification;
    }

    [HttpPost("verify")]
    public Task<ActionResult<VerifyCostShieldAuthorizationResponse>> Verify(
        [FromBody] VerifyCostShieldAuthorizationRequest request,
        CancellationToken ct)
    {
        return VerifyOrConsume(request, consume: false, ct);
    }

    [HttpPost("consume")]
    public Task<ActionResult<VerifyCostShieldAuthorizationResponse>> Consume(
        [FromBody] VerifyCostShieldAuthorizationRequest request,
        CancellationToken ct)
    {
        return VerifyOrConsume(request, consume: true, ct);
    }

    private async Task<ActionResult<VerifyCostShieldAuthorizationResponse>> VerifyOrConsume(
        VerifyCostShieldAuthorizationRequest request,
        bool consume,
        CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst("projectId")?.Value, out var projectId))
            return Unauthorized();

        Response.Headers.CacheControl = "no-store";
        var result = await _verification.VerifyAsync(
            projectId,
            request,
            new CostShieldRequestContext(
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.FirstOrDefault(),
                Request.Headers.Origin.FirstOrDefault()),
            consume,
            ct);

        if (result.Status == CostShieldFlowStatus.Ok)
            return Ok(result.Value);

        var error = new
        {
            error = result.Error?.Code ?? "costshield_error",
            error_description = result.Error?.Message ?? "The CostShield request failed."
        };

        return result.Status switch
        {
            CostShieldFlowStatus.BadRequest => BadRequest(error),
            CostShieldFlowStatus.Unauthorized => Unauthorized(error),
            CostShieldFlowStatus.Forbidden => StatusCode(
                StatusCodes.Status403Forbidden,
                error),
            CostShieldFlowStatus.NotFound => NotFound(error),
            CostShieldFlowStatus.Conflict => Conflict(error),
            CostShieldFlowStatus.RateLimited => StatusCode(
                StatusCodes.Status429TooManyRequests,
                error),
            _ => StatusCode(StatusCodes.Status500InternalServerError, error)
        };
    }
}
