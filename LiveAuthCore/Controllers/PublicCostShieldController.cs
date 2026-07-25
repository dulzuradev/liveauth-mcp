using LiveAuthCore.Models.CostShield;
using LiveAuthCore.Services.CostShield;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LiveAuthCore.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/public/costshield")]
public sealed class PublicCostShieldController : ControllerBase
{
    private const string PublicKeyHeaderName = "X-LW-Public";

    private readonly ICostShieldChallengeService _challenges;
    private readonly ICostShieldTokenService _tokens;

    public PublicCostShieldController(
        ICostShieldChallengeService challenges,
        ICostShieldTokenService tokens)
    {
        _challenges = challenges;
        _tokens = tokens;
    }

    [HttpPost("challenges")]
    public async Task<ActionResult<CostShieldChallengeResponse>> CreateChallenge(
        [FromBody] CreateCostShieldChallengeRequest request,
        CancellationToken ct)
    {
        var publicKey = ResolvePublicKey(request.ProjectPublicKey);
        if (publicKey.Error != null)
            return BadRequest(publicKey.Error);

        Response.Headers.CacheControl = "no-store";
        var result = await _challenges.CreateAsync(
            publicKey.Value!,
            request,
            GetRequestContext(),
            ct);

        return MapResult(result);
    }

    [HttpPost("challenges/{challengeId}/complete")]
    public async Task<ActionResult<CostShieldAuthorizationResponse>> CompleteChallenge(
        string challengeId,
        [FromBody] CompleteCostShieldChallengeRequest request,
        CancellationToken ct)
    {
        var publicKey = ResolvePublicKey(request.ProjectPublicKey);
        if (publicKey.Error != null)
            return BadRequest(publicKey.Error);

        Response.Headers.CacheControl = "no-store";
        var result = await _challenges.CompleteAsync(
            challengeId,
            publicKey.Value!,
            request,
            GetRequestContext(),
            ct);

        return MapResult(result);
    }

    [HttpGet(".well-known/jwks.json")]
    public ActionResult<CostShieldJwksResponse> GetJwks()
    {
        Response.Headers.CacheControl = "public, max-age=300";
        return Ok(_tokens.GetJwks());
    }

    private (string? Value, object? Error) ResolvePublicKey(string? bodyPublicKey)
    {
        var headerPublicKey = Request.Headers[PublicKeyHeaderName]
            .FirstOrDefault()
            ?.Trim();
        bodyPublicKey = bodyPublicKey?.Trim();

        if (!string.IsNullOrWhiteSpace(headerPublicKey) &&
            !string.IsNullOrWhiteSpace(bodyPublicKey) &&
            !string.Equals(headerPublicKey, bodyPublicKey, StringComparison.Ordinal))
        {
            return (null, new
            {
                error = "project_key_mismatch",
                error_description = "The header and request project keys must match."
            });
        }

        var publicKey = headerPublicKey ?? bodyPublicKey;
        if (string.IsNullOrWhiteSpace(publicKey) || publicKey.Length > 256)
        {
            return (null, new
            {
                error = "missing_project_key",
                error_description = "Provide a valid project public key in X-LW-Public or the request body."
            });
        }

        return (publicKey, null);
    }

    private CostShieldRequestContext GetRequestContext()
    {
        return new CostShieldRequestContext(
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.FirstOrDefault(),
            Request.Headers.Origin.FirstOrDefault());
    }

    private ActionResult<T> MapResult<T>(CostShieldFlowResult<T> result)
    {
        if (result.Status == CostShieldFlowStatus.Ok)
            return Ok(result.Value);

        if (result.Error?.RetryAfterSeconds is int retryAfterSeconds)
            Response.Headers.RetryAfter = retryAfterSeconds.ToString();

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
