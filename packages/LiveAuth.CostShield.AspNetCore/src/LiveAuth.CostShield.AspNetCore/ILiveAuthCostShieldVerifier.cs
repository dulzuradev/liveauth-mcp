namespace LiveAuth.CostShield.AspNetCore;

/// <summary>Validates and optionally consumes CostShield authorizations.</summary>
public interface ILiveAuthCostShieldVerifier
{
    /// <summary>
    /// Verifies a CostShield token locally without consuming it.
    /// </summary>
    Task<LiveAuthCostShieldClaims> VerifyAsync(
        string token,
        string action,
        string? origin = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies a token and consumes it remotely when required.
    /// </summary>
    Task<LiveAuthCostShieldAuthorization> AuthorizeAsync(
        string token,
        string action,
        string? origin = null,
        LiveAuthCostShieldConsumeMode consume =
            LiveAuthCostShieldConsumeMode.Auto,
        CancellationToken cancellationToken = default);
}
