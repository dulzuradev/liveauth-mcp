using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Models.CostShield;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Services.CostShield;

public interface ICostShieldVerificationService
{
    Task<CostShieldFlowResult<VerifyCostShieldAuthorizationResponse>> VerifyAsync(
        Guid projectId,
        VerifyCostShieldAuthorizationRequest request,
        CostShieldRequestContext context,
        bool consume,
        CancellationToken ct);
}

public sealed class CostShieldVerificationService : ICostShieldVerificationService
{
    private readonly LiveAuthDbContext _db;
    private readonly ICostShieldTokenService _tokens;
    private readonly IClientContextHasher _contextHasher;

    public CostShieldVerificationService(
        LiveAuthDbContext db,
        ICostShieldTokenService tokens,
        IClientContextHasher contextHasher)
    {
        _db = db;
        _tokens = tokens;
        _contextHasher = contextHasher;
    }

    public async Task<CostShieldFlowResult<VerifyCostShieldAuthorizationResponse>> VerifyAsync(
        Guid projectId,
        VerifyCostShieldAuthorizationRequest request,
        CostShieldRequestContext context,
        bool consume,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        if (request.Action?.Length > 100 ||
            request.Environment?.Length > 16 ||
            request.Origin?.Length > 512)
        {
            return CostShieldFlowResult<VerifyCostShieldAuthorizationResponse>.BadRequest(
                "invalid_expectation",
                "The expected action, environment, or origin is too large.");
        }

        var tokenValidation = _tokens.Validate(request.Token);
        if (!tokenValidation.IsValid || tokenValidation.Principal == null)
        {
            return CostShieldFlowResult<VerifyCostShieldAuthorizationResponse>.Unauthorized(
                tokenValidation.Error ?? "invalid_token",
                "The CostShield authorization token is invalid or expired.");
        }

        var claims = ReadRequiredClaims(tokenValidation.Principal);
        if (claims == null || claims.ProjectId != projectId)
        {
            return CostShieldFlowResult<VerifyCostShieldAuthorizationResponse>.Unauthorized(
                "invalid_token",
                "The CostShield authorization token is not valid for this project.");
        }

        var expectationError = ValidateExpectations(request, claims);
        if (expectationError != null)
        {
            return CostShieldFlowResult<VerifyCostShieldAuthorizationResponse>.Forbidden(
                expectationError.Value.Code,
                expectationError.Value.Message);
        }

        var authorization = await _db.CostShieldAuthorizations
            .Include(item => item.ProtectedAction)
            .FirstOrDefaultAsync(item =>
                item.ProjectId == projectId &&
                item.TokenId == claims.TokenId, ct);

        if (authorization == null ||
            authorization.ProtectedActionId != claims.ProtectedActionId ||
            !string.Equals(
                authorization.ProtectedAction.Name,
                claims.Action,
                StringComparison.Ordinal) ||
            !string.Equals(
                authorization.Environment,
                claims.Environment,
                StringComparison.Ordinal) ||
            !string.Equals(
                authorization.Origin,
                claims.Origin,
                StringComparison.Ordinal))
        {
            return CostShieldFlowResult<VerifyCostShieldAuthorizationResponse>.Unauthorized(
                "invalid_token",
                "The CostShield authorization token could not be matched to an issued authorization.");
        }

        var now = DateTime.UtcNow;
        if (authorization.ExpiresAt <= now)
        {
            return CostShieldFlowResult<VerifyCostShieldAuthorizationResponse>.Unauthorized(
                "token_expired",
                "The CostShield authorization token has expired.");
        }

        if (!string.Equals(
                authorization.Status,
                CostShieldAuthorizationStatuses.Active,
                StringComparison.Ordinal))
        {
            var code = string.Equals(
                authorization.Status,
                CostShieldAuthorizationStatuses.Consumed,
                StringComparison.Ordinal)
                ? "authorization_already_consumed"
                : "authorization_inactive";

            return CostShieldFlowResult<VerifyCostShieldAuthorizationResponse>.Conflict(
                code,
                "The CostShield authorization is no longer active.");
        }

        var consumed = false;
        var eventType = AuthEventType.CostShieldAuthorizationVerified;
        if (consume && authorization.RequireSingleUse)
        {
            authorization.Status = CostShieldAuthorizationStatuses.Consumed;
            authorization.ConsumedAt = now;
            authorization.ConcurrencyStamp = Guid.NewGuid().ToString("N");
            consumed = true;
            eventType = AuthEventType.CostShieldAuthorizationConsumed;
        }

        stopwatch.Stop();
        _db.AuthEvents.Add(new AuthEvent
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            ProtectedActionId = authorization.ProtectedActionId,
            EventType = eventType,
            Environment = authorization.Environment,
            CreatedAt = now,
            ClientIp = null,
            IpAddressHash = _contextHasher.HashIp(context.IpAddress),
            ClientContextHash = _contextHasher.HashContext(
                projectId,
                context.IpAddress,
                context.UserAgent,
                subject: null),
            VerificationMethod = authorization.VerificationMethod,
            DurationMilliseconds = checked((int)Math.Min(
                stopwatch.ElapsedMilliseconds,
                int.MaxValue)),
            EstimatedCostProtected = consume
                ? authorization.ProtectedAction.EstimatedCostPerExecution
                : null,
            Success = true,
            Reason = consume
                ? consumed ? "authorization_consumed" : "reusable_authorization_verified"
                : "authorization_verified"
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return CostShieldFlowResult<VerifyCostShieldAuthorizationResponse>.Conflict(
                "authorization_already_consumed",
                "The CostShield authorization has already been consumed.");
        }

        return CostShieldFlowResult<VerifyCostShieldAuthorizationResponse>.Ok(
            new VerifyCostShieldAuthorizationResponse(
                Verified: true,
                Consumed: consumed,
                AuthorizationId: authorization.Id,
                Action: authorization.ProtectedAction.Name,
                Environment: authorization.Environment,
                Origin: authorization.Origin,
                VerificationMethod: authorization.VerificationMethod,
                ExpiresAtUnix: new DateTimeOffset(authorization.ExpiresAt).ToUnixTimeSeconds(),
                RequireSingleUse: authorization.RequireSingleUse));
    }

    private static CostShieldTokenClaims? ReadRequiredClaims(ClaimsPrincipal principal)
    {
        var tokenId = principal.FindFirstValue(JwtRegisteredClaimNames.Jti);
        var action = principal.FindFirstValue("action");
        var environment = principal.FindFirstValue("environment");

        if (string.IsNullOrWhiteSpace(tokenId) ||
            string.IsNullOrWhiteSpace(action) ||
            string.IsNullOrWhiteSpace(environment) ||
            !Guid.TryParse(principal.FindFirstValue("projectId"), out var projectId) ||
            !Guid.TryParse(
                principal.FindFirstValue("protectedActionId"),
                out var protectedActionId))
        {
            return null;
        }

        return new CostShieldTokenClaims(
            tokenId,
            projectId,
            protectedActionId,
            action,
            environment,
            principal.FindFirstValue("origin"));
    }

    private static (string Code, string Message)? ValidateExpectations(
        VerifyCostShieldAuthorizationRequest request,
        CostShieldTokenClaims claims)
    {
        if (!string.IsNullOrWhiteSpace(request.Action) &&
            !string.Equals(request.Action.Trim(), claims.Action, StringComparison.Ordinal))
        {
            return (
                "action_mismatch",
                "The authorization token is not valid for the expected action.");
        }

        if (!string.IsNullOrWhiteSpace(request.Environment) &&
            !string.Equals(
                request.Environment.Trim().ToUpperInvariant(),
                claims.Environment,
                StringComparison.Ordinal))
        {
            return (
                "environment_mismatch",
                "The authorization token is not valid for the expected environment.");
        }

        if (!string.IsNullOrWhiteSpace(request.Origin))
        {
            var normalizedOrigin = NormalizeOrigin(request.Origin);
            if (normalizedOrigin == null)
            {
                return (
                    "invalid_origin",
                    "The expected origin must be an absolute HTTP or HTTPS origin.");
            }

            if (!string.Equals(normalizedOrigin, claims.Origin, StringComparison.Ordinal))
            {
                return (
                    "origin_mismatch",
                    "The authorization token is not valid for the expected origin.");
            }
        }

        return null;
    }

    private static string? NormalizeOrigin(string origin)
    {
        if (!Uri.TryCreate(origin.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath != "/")
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }
}

public sealed record CostShieldTokenClaims(
    string TokenId,
    Guid ProjectId,
    Guid ProtectedActionId,
    string Action,
    string Environment,
    string? Origin);
