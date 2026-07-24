using System.Text.RegularExpressions;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Models.CostShield;

namespace LiveAuthCore.Services.CostShield;

public static class ProtectedActionPolicy
{
    private const int MinimumDifficulty = 8;
    private const int MaximumDifficulty = 24;
    private const int MaximumRequestLimit = 1_000_000;
    private const int MinimumWindowSeconds = 60;
    private const int MaximumWindowSeconds = 2_592_000;
    private const int MinimumTokenLifetimeSeconds = 30;
    private const int MaximumTokenLifetimeSeconds = 3_600;
    private const int MaximumOrigins = 50;
    private const int MaximumLightningPriceSats = 1_000_000;
    private const decimal MaximumEstimatedCost = 10_000m;

    private static readonly Regex ActionNamePattern = new(
        "^[a-z][a-z0-9]*(?:[._-][a-z0-9]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static ProtectedActionPolicyResult Evaluate(UpsertProtectedActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var environment = (request.Environment ?? string.Empty).Trim().ToUpperInvariant();
        var name = (request.Name ?? string.Empty).Trim().ToLowerInvariant();
        var displayName = (request.DisplayName ?? string.Empty).Trim();
        var description = (request.Description ?? string.Empty).Trim();
        var failureBehavior = NormalizeKnownValue(
            request.FailureBehavior,
            ProtectedActionFailureBehaviors.Deny,
            ProtectedActionFailureBehaviors.LightningFallback);
        var lightningFallbackMode = NormalizeKnownValue(
            request.LightningFallbackMode,
            ProtectedActionLightningModes.RateLimitOnly,
            ProtectedActionLightningModes.Always);

        if (environment is not ("TEST" or "LIVE"))
            AddError(errors, nameof(request.Environment), "Environment must be TEST or LIVE.");

        if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || !ActionNamePattern.IsMatch(name))
        {
            AddError(
                errors,
                nameof(request.Name),
                "Name must be 1-100 lowercase letters, numbers, dots, underscores, or hyphens and start with a letter.");
        }

        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 120)
            AddError(errors, nameof(request.DisplayName), "DisplayName is required and must be 120 characters or less.");

        if (description.Length > 500)
            AddError(errors, nameof(request.Description), "Description must be 500 characters or less.");

        if (request.BaseDifficulty is < MinimumDifficulty or > MaximumDifficulty)
            AddError(errors, nameof(request.BaseDifficulty), $"BaseDifficulty must be between {MinimumDifficulty} and {MaximumDifficulty}.");

        if (request.SuspiciousDifficulty is < MinimumDifficulty or > MaximumDifficulty)
        {
            AddError(
                errors,
                nameof(request.SuspiciousDifficulty),
                $"SuspiciousDifficulty must be between {MinimumDifficulty} and {MaximumDifficulty}.");
        }

        if (request.MaximumDifficulty is < MinimumDifficulty or > MaximumDifficulty)
        {
            AddError(
                errors,
                nameof(request.MaximumDifficulty),
                $"MaximumDifficulty must be between {MinimumDifficulty} and {MaximumDifficulty}.");
        }

        if (request.BaseDifficulty > request.SuspiciousDifficulty ||
            request.SuspiciousDifficulty > request.MaximumDifficulty)
        {
            AddError(
                errors,
                nameof(request.BaseDifficulty),
                "Difficulty bounds must satisfy base <= suspicious <= maximum.");
        }

        ValidateLimit(
            errors,
            nameof(request.AnonymousRequestLimit),
            request.AnonymousRequestLimit,
            nameof(request.AnonymousLimitWindowSeconds),
            request.AnonymousLimitWindowSeconds);

        if (request.AuthenticatedRequestLimit.HasValue != request.AuthenticatedLimitWindowSeconds.HasValue)
        {
            AddError(
                errors,
                nameof(request.AuthenticatedRequestLimit),
                "AuthenticatedRequestLimit and AuthenticatedLimitWindowSeconds must either both be set or both be omitted.");
        }
        else if (request.AuthenticatedRequestLimit.HasValue &&
                 request.AuthenticatedLimitWindowSeconds.HasValue)
        {
            ValidateLimit(
                errors,
                nameof(request.AuthenticatedRequestLimit),
                request.AuthenticatedRequestLimit.Value,
                nameof(request.AuthenticatedLimitWindowSeconds),
                request.AuthenticatedLimitWindowSeconds.Value);
        }

        if (request.TokenLifetimeSeconds is < MinimumTokenLifetimeSeconds or > MaximumTokenLifetimeSeconds)
        {
            AddError(
                errors,
                nameof(request.TokenLifetimeSeconds),
                $"TokenLifetimeSeconds must be between {MinimumTokenLifetimeSeconds} and {MaximumTokenLifetimeSeconds}.");
        }

        var origins = new List<string>();
        var suppliedOrigins = request.AllowedOrigins ?? new List<string>();
        if (suppliedOrigins.Count > MaximumOrigins)
            AddError(errors, nameof(request.AllowedOrigins), $"No more than {MaximumOrigins} allowed origins may be configured.");

        foreach (var value in suppliedOrigins)
        {
            if (!TryNormalizeOrigin(value, out var normalizedOrigin))
            {
                AddError(
                    errors,
                    nameof(request.AllowedOrigins),
                    $"'{value}' is not a valid HTTPS/HTTP origin or hostname.");
                continue;
            }

            if (!origins.Contains(normalizedOrigin, StringComparer.OrdinalIgnoreCase))
                origins.Add(normalizedOrigin);
        }

        if (failureBehavior == null)
        {
            AddError(
                errors,
                nameof(request.FailureBehavior),
                $"FailureBehavior must be {ProtectedActionFailureBehaviors.Deny} or {ProtectedActionFailureBehaviors.LightningFallback}.");
            failureBehavior = ProtectedActionFailureBehaviors.Deny;
        }

        if (lightningFallbackMode == null)
        {
            AddError(
                errors,
                nameof(request.LightningFallbackMode),
                $"LightningFallbackMode must be {ProtectedActionLightningModes.RateLimitOnly} or {ProtectedActionLightningModes.Always}.");
            lightningFallbackMode = ProtectedActionLightningModes.RateLimitOnly;
        }

        if (request.LightningPriceSats < 0 || request.LightningPriceSats > MaximumLightningPriceSats)
        {
            AddError(
                errors,
                nameof(request.LightningPriceSats),
                $"LightningPriceSats must be between 0 and {MaximumLightningPriceSats}.");
        }
        else if (request.AllowLightningFallback && request.LightningPriceSats == 0)
        {
            AddError(
                errors,
                nameof(request.LightningPriceSats),
                "LightningPriceSats must be at least 1 when Lightning fallback is enabled.");
        }

        if (!request.AllowLightningFallback &&
            failureBehavior == ProtectedActionFailureBehaviors.LightningFallback)
        {
            AddError(
                errors,
                nameof(request.FailureBehavior),
                "FailureBehavior cannot be LightningFallback when Lightning fallback is disabled.");
        }

        if (request.EstimatedCostPerExecution is < 0 or > MaximumEstimatedCost)
        {
            AddError(
                errors,
                nameof(request.EstimatedCostPerExecution),
                $"EstimatedCostPerExecution must be between 0 and {MaximumEstimatedCost}.");
        }

        var normalized = new UpsertProtectedActionRequest
        {
            Environment = environment,
            Name = name,
            DisplayName = displayName,
            Description = description,
            IsEnabled = request.IsEnabled,
            BaseDifficulty = request.BaseDifficulty,
            SuspiciousDifficulty = request.SuspiciousDifficulty,
            MaximumDifficulty = request.MaximumDifficulty,
            AnonymousRequestLimit = request.AnonymousRequestLimit,
            AnonymousLimitWindowSeconds = request.AnonymousLimitWindowSeconds,
            AuthenticatedRequestLimit = request.AuthenticatedRequestLimit,
            AuthenticatedLimitWindowSeconds = request.AuthenticatedLimitWindowSeconds,
            RequireSingleUseToken = request.RequireSingleUseToken,
            TokenLifetimeSeconds = request.TokenLifetimeSeconds,
            AllowedOrigins = origins,
            FailureBehavior = failureBehavior,
            AllowLightningFallback = request.AllowLightningFallback,
            LightningPriceSats = request.LightningPriceSats,
            LightningFallbackMode = lightningFallbackMode,
            LightningBypassesProofOfWork = request.LightningBypassesProofOfWork,
            EstimatedCostPerExecution = request.EstimatedCostPerExecution
        };

        return new ProtectedActionPolicyResult(
            normalized,
            errors.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToArray(),
                StringComparer.OrdinalIgnoreCase));
    }

    public static bool TryNormalizeOrigin(string? value, out string normalized)
    {
        normalized = string.Empty;
        var candidate = value?.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > 512)
            return false;

        if (candidate.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
                string.IsNullOrWhiteSpace(uri.Host) ||
                (uri.AbsolutePath != "/" && !string.IsNullOrEmpty(uri.AbsolutePath)) ||
                !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment) ||
                !string.IsNullOrEmpty(uri.UserInfo))
            {
                return false;
            }

            normalized = uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
            return true;
        }

        if (candidate.Contains('/') ||
            candidate.Contains('?') ||
            candidate.Contains('#') ||
            candidate.Contains('@') ||
            !Uri.TryCreate($"https://{candidate}", UriKind.Absolute, out var hostnameUri) ||
            string.IsNullOrWhiteSpace(hostnameUri.Host) ||
            (Uri.CheckHostName(hostnameUri.Host) == UriHostNameType.Unknown &&
             !string.Equals(hostnameUri.Host, "localhost", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        normalized = candidate.ToLowerInvariant();
        return true;
    }

    private static void ValidateLimit(
        Dictionary<string, List<string>> errors,
        string limitField,
        int limit,
        string windowField,
        int windowSeconds)
    {
        if (limit is < 1 or > MaximumRequestLimit)
            AddError(errors, limitField, $"Request limit must be between 1 and {MaximumRequestLimit}.");

        if (windowSeconds is < MinimumWindowSeconds or > MaximumWindowSeconds)
        {
            AddError(
                errors,
                windowField,
                $"Limit window must be between {MinimumWindowSeconds} and {MaximumWindowSeconds} seconds.");
        }
    }

    private static string? NormalizeKnownValue(string? value, params string[] allowed)
    {
        var candidate = value?.Trim();
        return allowed.FirstOrDefault(item =>
            string.Equals(item, candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static void AddError(
        Dictionary<string, List<string>> errors,
        string field,
        string message)
    {
        if (!errors.TryGetValue(field, out var messages))
        {
            messages = new List<string>();
            errors[field] = messages;
        }

        messages.Add(message);
    }
}

public sealed record ProtectedActionPolicyResult(
    UpsertProtectedActionRequest Normalized,
    IReadOnlyDictionary<string, string[]> Errors)
{
    public bool IsValid => Errors.Count == 0;
}
