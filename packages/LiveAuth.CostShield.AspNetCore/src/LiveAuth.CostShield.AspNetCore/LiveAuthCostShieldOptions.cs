using Microsoft.Extensions.Options;

namespace LiveAuth.CostShield.AspNetCore;

/// <summary>
/// Configures CostShield token validation for one LiveAuth project.
/// </summary>
public sealed class LiveAuthCostShieldOptions
{
    /// <summary>The default LiveAuth API URL.</summary>
    public const string DefaultApiUrl = "https://api.liveauth.app";

    /// <summary>The default CostShield JWT issuer.</summary>
    public const string DefaultIssuer = "https://api.liveauth.app";

    /// <summary>The default CostShield JWT audience.</summary>
    public const string DefaultAudience = "liveauth-costshield";

    /// <summary>The LiveAuth project identifier.</summary>
    public Guid ProjectId { get; set; }

    /// <summary>The protected-action environment.</summary>
    public LiveAuthCostShieldEnvironment Environment { get; set; }
        = LiveAuthCostShieldEnvironment.Test;

    /// <summary>
    /// The project secret key. Required when single-use tokens are consumed.
    /// </summary>
    public string? SecretKey { get; set; }

    /// <summary>The LiveAuth API base URL.</summary>
    public Uri ApiUrl { get; set; } = new(DefaultApiUrl);

    /// <summary>The expected CostShield JWT issuer.</summary>
    public string Issuer { get; set; } = DefaultIssuer;

    /// <summary>The expected CostShield JWT audience.</summary>
    public string Audience { get; set; } = DefaultAudience;

    /// <summary>Allowed clock skew during token validation.</summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long public signing keys remain cached.</summary>
    public TimeSpan JwksCacheDuration { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>The LiveAuth environment that issued a token.</summary>
public enum LiveAuthCostShieldEnvironment
{
    /// <summary>The non-production test environment.</summary>
    Test,

    /// <summary>The production environment.</summary>
    Live
}

/// <summary>Controls when authorization is confirmed with LiveAuth.</summary>
public enum LiveAuthCostShieldConsumeMode
{
    /// <summary>Consume single-use tokens and verify reusable tokens locally.</summary>
    Auto,

    /// <summary>Always confirm authorization remotely.</summary>
    Always,

    /// <summary>Never make a remote consumption request.</summary>
    Never
}

internal sealed class LiveAuthCostShieldOptionsValidator
    : IValidateOptions<LiveAuthCostShieldOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        LiveAuthCostShieldOptions options)
    {
        var failures = new List<string>();
        if (options.ProjectId == Guid.Empty)
            failures.Add("ProjectId is required.");

        if (!Enum.IsDefined(options.Environment))
            failures.Add("Environment must be Test or Live.");

        if (!IsSafeApiUrl(options.ApiUrl))
            failures.Add("ApiUrl must be an absolute HTTP or HTTPS URL without credentials.");

        if (string.IsNullOrWhiteSpace(options.Issuer))
            failures.Add("Issuer is required.");

        if (string.IsNullOrWhiteSpace(options.Audience))
            failures.Add("Audience is required.");

        if (options.ClockSkew < TimeSpan.Zero ||
            options.ClockSkew > TimeSpan.FromMinutes(5))
        {
            failures.Add("ClockSkew must be between zero and five minutes.");
        }

        if (options.JwksCacheDuration < TimeSpan.FromSeconds(1) ||
            options.JwksCacheDuration > TimeSpan.FromHours(1))
        {
            failures.Add("JwksCacheDuration must be between one second and one hour.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsSafeApiUrl(Uri? uri)
        => uri is { IsAbsoluteUri: true } &&
           uri.Scheme is "http" or "https" &&
           string.IsNullOrEmpty(uri.UserInfo);
}

internal static class LiveAuthCostShieldEnvironmentExtensions
{
    public static string ToProtocolValue(
        this LiveAuthCostShieldEnvironment environment)
        => environment == LiveAuthCostShieldEnvironment.Live
            ? "LIVE"
            : "TEST";
}
