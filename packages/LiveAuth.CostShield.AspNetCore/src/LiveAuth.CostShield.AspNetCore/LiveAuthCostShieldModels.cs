using System.Security.Claims;

namespace LiveAuth.CostShield.AspNetCore;

/// <summary>Validated, action-bound claims from a CostShield token.</summary>
public sealed record LiveAuthCostShieldClaims(
    string TokenId,
    Guid ProjectId,
    string ProjectPublicKey,
    Guid ProtectedActionId,
    string Environment,
    string Action,
    string? Origin,
    string VerificationMethod,
    int Difficulty,
    string ClientContextHash,
    bool SingleUse,
    int ConfigurationVersion,
    string? ClientSubject,
    long IssuedAtUnix,
    long ExpiresAtUnix,
    ClaimsPrincipal Principal);

/// <summary>LiveAuth's result after remote verification or consumption.</summary>
public sealed record LiveAuthCostShieldRemoteResult(
    bool Verified,
    bool Consumed,
    Guid AuthorizationId,
    string Action,
    string Environment,
    string? Origin,
    string VerificationMethod,
    long ExpiresAtUnix,
    bool RequireSingleUse);

/// <summary>
/// The authorization attached to an ASP.NET Core request after validation.
/// </summary>
public sealed record LiveAuthCostShieldAuthorization(
    LiveAuthCostShieldClaims Claims,
    LiveAuthCostShieldRemoteResult? Remote);

/// <summary>
/// Feature exposed through
/// <see cref="Microsoft.AspNetCore.Http.HttpContext.Features"/>.
/// </summary>
public interface ILiveAuthCostShieldFeature
{
    /// <summary>The authorization established for this request.</summary>
    LiveAuthCostShieldAuthorization Authorization { get; }
}

internal sealed record LiveAuthCostShieldFeature(
    LiveAuthCostShieldAuthorization Authorization)
    : ILiveAuthCostShieldFeature;

internal sealed class RemoteAuthorizationResponse
{
    public bool Verified { get; set; }
    public bool Consumed { get; set; }
    public Guid AuthorizationId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string? Origin { get; set; }
    public string VerificationMethod { get; set; } = string.Empty;
    public long ExpiresAtUnix { get; set; }
    public bool RequireSingleUse { get; set; }
}

internal sealed class LiveAuthErrorResponse
{
    public string? Error { get; set; }
    public string? ErrorDescription { get; set; }
    public string? Message { get; set; }
}
