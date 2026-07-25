using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Models.CostShield;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace LiveAuthCore.Services.CostShield;

public interface ICostShieldChallengeService
{
    Task<CostShieldFlowResult<CostShieldChallengeResponse>> CreateAsync(
        string publicKey,
        CreateCostShieldChallengeRequest request,
        CostShieldRequestContext requestContext,
        CancellationToken ct);

    Task<CostShieldFlowResult<CostShieldAuthorizationResponse>> CompleteAsync(
        string challengeId,
        string publicKey,
        CompleteCostShieldChallengeRequest request,
        CostShieldRequestContext requestContext,
        CancellationToken ct);
}

public sealed class CostShieldChallengeService : ICostShieldChallengeService
{
    private readonly LiveAuthDbContext _db;
    private readonly ApiKeyService _apiKeys;
    private readonly PowChallengeSigner _signer;
    private readonly PowRateLimitService _burstRateLimit;
    private readonly IClientContextHasher _contextHasher;
    private readonly ICostShieldTokenService _tokens;
    private readonly IConfiguration _configuration;

    public CostShieldChallengeService(
        LiveAuthDbContext db,
        ApiKeyService apiKeys,
        PowChallengeSigner signer,
        PowRateLimitService burstRateLimit,
        IClientContextHasher contextHasher,
        ICostShieldTokenService tokens,
        IConfiguration configuration)
    {
        _db = db;
        _apiKeys = apiKeys;
        _signer = signer;
        _burstRateLimit = burstRateLimit;
        _contextHasher = contextHasher;
        _tokens = tokens;
        _configuration = configuration;
    }

    public async Task<CostShieldFlowResult<CostShieldChallengeResponse>> CreateAsync(
        string publicKey,
        CreateCostShieldChallengeRequest request,
        CostShieldRequestContext requestContext,
        CancellationToken ct)
    {
        var inputError = ValidateRequestInput(request.Action, request.Environment, request.Subject, request.ClientMetadata);
        if (inputError == null && request.RiskHint?.Length > 32)
            inputError = ("invalid_risk_hint", "RiskHint must be 32 characters or less.");

        if (inputError != null)
            return CostShieldFlowResult<CostShieldChallengeResponse>.BadRequest(inputError.Value.Code, inputError.Value.Message);

        var resolved = await ResolveActionAsync(publicKey, request.Environment, request.Action, ct);
        if (resolved.Error != null)
            return CostShieldFlowResult<CostShieldChallengeResponse>.FromError(resolved.Error);

        var project = resolved.Project!;
        var action = resolved.Action!;
        var origin = ResolveOrigin(requestContext.Origin, request.Origin);
        if (!origin.IsValid || !IsOriginAllowed(project, action, origin.Value))
        {
            await LogEventAsync(
                project,
                action,
                AuthEventType.CostShieldInvalidOrigin,
                success: false,
                requestContext,
                origin.Value,
                request.Subject,
                "invalid_origin",
                ct);
            return CostShieldFlowResult<CostShieldChallengeResponse>.Forbidden(
                "invalid_origin",
                "The request origin is not allowed for this protected action.");
        }

        var ipHash = _contextHasher.HashIp(requestContext.IpAddress);
        var subjectHash = string.IsNullOrWhiteSpace(request.Subject)
            ? null
            : _contextHasher.HashSubject(request.Subject);

        if (!_burstRateLimit.TryAcquire(ipHash, project.Id))
        {
            await LogEventAsync(
                project,
                action,
                AuthEventType.CostShieldRateLimited,
                success: false,
                requestContext,
                origin.Value,
                request.Subject,
                "burst_limit",
                ct);
            return CostShieldFlowResult<CostShieldChallengeResponse>.RateLimited(
                "burst_rate_limit",
                "Too many challenge requests. Try again later.",
                retryAfterSeconds: 60);
        }

        var rate = await GetRateStateAsync(action, ipHash, subjectHash, ct);
        if (rate.Exceeded)
        {
            await LogEventAsync(
                project,
                action,
                AuthEventType.CostShieldRateLimited,
                success: false,
                requestContext,
                origin.Value,
                request.Subject,
                rate.Reason,
                ct);
            return CostShieldFlowResult<CostShieldChallengeResponse>.RateLimited(
                "action_rate_limit",
                "The protected action rate limit has been reached.",
                rate.RetryAfterSeconds);
        }

        var difficulty = SelectDifficulty(action, request.RiskHint, rate);
        var challengeId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16))
            .ToLowerInvariant();
        var lifetimeSeconds = Math.Clamp(
            _configuration.GetValue("CostShield:ChallengeLifetimeSeconds", 120),
            30,
            300);
        var expiresAtUnix = DateTimeOffset.UtcNow.AddSeconds(lifetimeSeconds).ToUnixTimeSeconds();
        var clientContextHash = _contextHasher.HashContext(
            project.Id,
            requestContext.IpAddress,
            requestContext.UserAgent,
            request.Subject);

        var payload = CostShieldChallengePayload.Build(
            project.Id,
            action.Id,
            action.Environment,
            action.Name,
            origin.Value,
            clientContextHash,
            subjectHash,
            challengeId,
            difficulty.Bits,
            expiresAtUnix,
            action.ConfigurationVersion);
        var signature = _signer.Sign(payload);

        _db.AuthEvents.Add(CreateEvent(
            project,
            action,
            AuthEventType.CostShieldChallengeIssued,
            success: true,
            requestContext,
            clientContextHash,
            ipHash,
            subjectHash,
            reason: difficulty.Reason,
            metadata: new
            {
                challengeId,
                difficulty = difficulty.Bits,
                configurationVersion = action.ConfigurationVersion
            }));
        await _db.SaveChangesAsync(ct);

        var targetHex = Convert.ToHexString(PowDifficulty.TargetFromBits(difficulty.Bits))
            .ToLowerInvariant();
        return CostShieldFlowResult<CostShieldChallengeResponse>.Ok(
            new CostShieldChallengeResponse(
                challengeId,
                project.PublicKey,
                action.Environment,
                action.Name,
                action.Id,
                targetHex,
                difficulty.Bits,
                difficulty.Reason,
                expiresAtUnix,
                action.ConfigurationVersion,
                signature));
    }

    public async Task<CostShieldFlowResult<CostShieldAuthorizationResponse>> CompleteAsync(
        string challengeId,
        string publicKey,
        CompleteCostShieldChallengeRequest request,
        CostShieldRequestContext requestContext,
        CancellationToken ct)
    {
        var inputError = ValidateRequestInput(request.Action, request.Environment, request.Subject, metadata: null);
        if (inputError != null ||
            !IsChallengeIdValid(challengeId) ||
            request.Nonce < 0 ||
            !IsChallengeSignatureValid(request.Signature))
        {
            return CostShieldFlowResult<CostShieldAuthorizationResponse>.BadRequest(
                inputError?.Code ?? "invalid_challenge",
                inputError?.Message ?? "The challenge completion payload is invalid.");
        }

        var resolved = await ResolveActionAsync(publicKey, request.Environment, request.Action, ct);
        if (resolved.Error != null)
            return CostShieldFlowResult<CostShieldAuthorizationResponse>.FromError(resolved.Error);

        var project = resolved.Project!;
        var action = resolved.Action!;
        var origin = ResolveOrigin(requestContext.Origin, request.Origin);
        if (!origin.IsValid || !IsOriginAllowed(project, action, origin.Value))
        {
            await LogEventAsync(
                project,
                action,
                AuthEventType.CostShieldInvalidOrigin,
                success: false,
                requestContext,
                origin.Value,
                request.Subject,
                "invalid_origin",
                ct);
            return CostShieldFlowResult<CostShieldAuthorizationResponse>.Forbidden(
                "invalid_origin",
                "The request origin is not allowed for this protected action.");
        }

        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (request.ExpiresAtUnix <= nowUnix)
        {
            await LogFailureAsync(project, action, requestContext, origin.Value, request.Subject, "challenge_expired", ct);
            return CostShieldFlowResult<CostShieldAuthorizationResponse>.Unauthorized(
                "challenge_expired",
                "The challenge has expired.");
        }

        if (request.ExpiresAtUnix > nowUnix + 300 ||
            request.ConfigurationVersion != action.ConfigurationVersion ||
            request.DifficultyBits < action.BaseDifficulty ||
            request.DifficultyBits > action.MaximumDifficulty)
        {
            await LogFailureAsync(project, action, requestContext, origin.Value, request.Subject, "invalid_challenge", ct);
            return CostShieldFlowResult<CostShieldAuthorizationResponse>.Unauthorized(
                "invalid_challenge",
                "The challenge is invalid or stale.");
        }

        var ipHash = _contextHasher.HashIp(requestContext.IpAddress);
        var subjectHash = string.IsNullOrWhiteSpace(request.Subject)
            ? null
            : _contextHasher.HashSubject(request.Subject);
        var clientContextHash = _contextHasher.HashContext(
            project.Id,
            requestContext.IpAddress,
            requestContext.UserAgent,
            request.Subject);
        var payload = CostShieldChallengePayload.Build(
            project.Id,
            action.Id,
            action.Environment,
            action.Name,
            origin.Value,
            clientContextHash,
            subjectHash,
            challengeId,
            request.DifficultyBits,
            request.ExpiresAtUnix,
            request.ConfigurationVersion);

        if (!_signer.Verify(payload, request.Signature))
        {
            await LogFailureAsync(project, action, requestContext, origin.Value, request.Subject, "invalid_signature", ct);
            return CostShieldFlowResult<CostShieldAuthorizationResponse>.Unauthorized(
                "invalid_signature",
                "The challenge signature is invalid.");
        }

        var hash = SHA256.HashData(
            Encoding.UTF8.GetBytes($"{project.PublicKey}:{challengeId}:{request.Nonce}"));
        if (!PowDifficulty.IsValid(hash, PowDifficulty.TargetFromBits(request.DifficultyBits)))
        {
            await LogFailureAsync(project, action, requestContext, origin.Value, request.Subject, "invalid_pow", ct);
            return CostShieldFlowResult<CostShieldAuthorizationResponse>.Unauthorized(
                "invalid_pow",
                "The proof-of-work solution does not meet the challenge target.");
        }

        var rate = await GetRateStateAsync(action, ipHash, subjectHash, ct);
        if (rate.Count > rate.Limit)
        {
            await LogEventAsync(
                project,
                action,
                AuthEventType.CostShieldRateLimited,
                success: false,
                requestContext,
                origin.Value,
                request.Subject,
                rate.Reason,
                ct);
            return CostShieldFlowResult<CostShieldAuthorizationResponse>.RateLimited(
                "action_rate_limit",
                "The protected action rate limit has been reached.",
                rate.RetryAfterSeconds);
        }

        if (await _db.CostShieldAuthorizations.AnyAsync(
                authorization =>
                    authorization.ProjectId == project.Id &&
                    authorization.ChallengeId == challengeId,
                ct))
        {
            await LogReplayAsync(project, action, requestContext, clientContextHash, ipHash, subjectHash, ct);
            return CostShieldFlowResult<CostShieldAuthorizationResponse>.Conflict(
                "challenge_replayed",
                "This challenge has already been completed.");
        }

        var issuedAt = DateTime.UtcNow;
        var authorization = new CostShieldAuthorization
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            ProtectedActionId = action.Id,
            ChallengeId = challengeId,
            TokenId = Guid.NewGuid().ToString("N"),
            Environment = action.Environment,
            VerificationMethod = "pow",
            Difficulty = request.DifficultyBits,
            Origin = origin.Value,
            ClientContextHash = clientContextHash,
            SubjectHash = subjectHash,
            RequireSingleUse = action.RequireSingleUseToken,
            ConfigurationVersion = action.ConfigurationVersion,
            Status = CostShieldAuthorizationStatuses.Active,
            IssuedAt = issuedAt,
            ExpiresAt = issuedAt.AddSeconds(action.TokenLifetimeSeconds),
            ConcurrencyStamp = Guid.NewGuid().ToString("N")
        };

        _db.CostShieldAuthorizations.Add(authorization);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            _db.Entry(authorization).State = EntityState.Detached;
            await LogReplayAsync(project, action, requestContext, clientContextHash, ipHash, subjectHash, ct);
            return CostShieldFlowResult<CostShieldAuthorizationResponse>.Conflict(
                "challenge_replayed",
                "This challenge has already been completed.");
        }

        var token = _tokens.Issue(authorization, project, action, request.Subject);
        _db.AuthEvents.AddRange(
            CreateEvent(
                project,
                action,
                AuthEventType.CostShieldChallengeCompleted,
                success: true,
                requestContext,
                clientContextHash,
                ipHash,
                subjectHash,
                reason: "pow",
                metadata: new { challengeId, authorizationId = authorization.Id }),
            CreateEvent(
                project,
                action,
                AuthEventType.CostShieldAuthorizationIssued,
                success: true,
                requestContext,
                clientContextHash,
                ipHash,
                subjectHash,
                reason: "pow",
                metadata: new
                {
                    authorizationId = authorization.Id,
                    tokenId = authorization.TokenId,
                    singleUse = authorization.RequireSingleUse
                }));
        await _db.SaveChangesAsync(ct);

        return CostShieldFlowResult<CostShieldAuthorizationResponse>.Ok(
            new CostShieldAuthorizationResponse(
                token,
                "Bearer",
                new DateTimeOffset(authorization.ExpiresAt).ToUnixTimeSeconds(),
                authorization.Id,
                action.Name,
                action.Environment,
                authorization.RequireSingleUse));
    }

    private async Task<(Project? Project, ProtectedAction? Action, CostShieldFlowError? Error)> ResolveActionAsync(
        string publicKey,
        string environment,
        string actionName,
        CancellationToken ct)
    {
        var auth = await _apiKeys.ResolveActivePublicProjectAsync(publicKey, ct);
        if (auth.Status != ApiKeyAuthStatus.Ok || auth.Project == null)
        {
            return (null, null, new CostShieldFlowError(
                CostShieldFlowStatus.Unauthorized,
                "invalid_project_key",
                "The project public key is invalid."));
        }

        var project = auth.Project;
        var normalizedEnvironment = environment.Trim().ToUpperInvariant();
        if (!string.Equals(
                (project.Environment ?? "TEST").Trim().ToUpperInvariant(),
                normalizedEnvironment,
                StringComparison.Ordinal))
        {
            return (null, null, new CostShieldFlowError(
                CostShieldFlowStatus.Forbidden,
                "environment_mismatch",
                "The requested environment is not active for this project."));
        }

        var normalizedAction = actionName.Trim().ToLowerInvariant();
        var action = await _db.ProtectedActions
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate =>
                candidate.ProjectId == project.Id &&
                candidate.Environment == normalizedEnvironment &&
                candidate.Name == normalizedAction &&
                candidate.IsEnabled,
                ct);

        if (action == null)
        {
            return (null, null, new CostShieldFlowError(
                CostShieldFlowStatus.NotFound,
                "protected_action_not_found",
                "The protected action was not found or is disabled."));
        }

        return (project, action, null);
    }

    private async Task<CostShieldRateState> GetRateStateAsync(
        ProtectedAction action,
        string ipHash,
        string? subjectHash,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var issuedChallenges = _db.AuthEvents.Where(candidate =>
            candidate.ProtectedActionId == action.Id &&
            candidate.EventType == AuthEventType.CostShieldChallengeIssued);

        var ipCutoff = now.AddSeconds(-action.AnonymousLimitWindowSeconds);
        var ipCount = await issuedChallenges.CountAsync(candidate =>
            candidate.CreatedAt >= ipCutoff &&
            candidate.IpAddressHash == ipHash, ct);
        var states = new List<CostShieldRateState>
        {
            BuildRateState(
                ipCount,
                action.AnonymousRequestLimit,
                action.AnonymousLimitWindowSeconds,
                "anonymous_ip_limit")
        };

        // A client-supplied subject is an additional limiter, never a
        // replacement for the IP limit. This prevents rotating subject values
        // from bypassing the anonymous allowance.
        if (!string.IsNullOrWhiteSpace(subjectHash) &&
            action.AuthenticatedRequestLimit.HasValue &&
            action.AuthenticatedLimitWindowSeconds.HasValue)
        {
            var subjectWindow = action.AuthenticatedLimitWindowSeconds.Value;
            var subjectCutoff = now.AddSeconds(-subjectWindow);
            var subjectCount = await issuedChallenges.CountAsync(candidate =>
                candidate.CreatedAt >= subjectCutoff &&
                candidate.SubjectHash == subjectHash, ct);
            states.Add(BuildRateState(
                subjectCount,
                action.AuthenticatedRequestLimit.Value,
                subjectWindow,
                "subject_limit"));
        }

        return states
            .OrderByDescending(state => (decimal)state.Count / state.Limit)
            .First();
    }

    private static CostShieldRateState BuildRateState(
        int count,
        int limit,
        int windowSeconds,
        string reason)
    {
        return new CostShieldRateState(
            Count: count,
            Limit: limit,
            Exceeded: count >= limit,
            NearLimit: count >= Math.Max(1, (int)Math.Ceiling(limit * 0.75m)),
            RetryAfterSeconds: Math.Max(1, windowSeconds),
            Reason: reason);
    }

    private static CostShieldDifficultyDecision SelectDifficulty(
        ProtectedAction action,
        string? riskHint,
        CostShieldRateState rate)
    {
        if (string.Equals(riskHint?.Trim(), "high", StringComparison.OrdinalIgnoreCase))
            return new CostShieldDifficultyDecision(action.MaximumDifficulty, "explicit_high_risk");

        if (string.Equals(riskHint?.Trim(), "suspicious", StringComparison.OrdinalIgnoreCase))
            return new CostShieldDifficultyDecision(action.SuspiciousDifficulty, "explicit_suspicious_risk");

        if (rate.NearLimit)
            return new CostShieldDifficultyDecision(action.SuspiciousDifficulty, "rate_limit_proximity");

        return new CostShieldDifficultyDecision(action.BaseDifficulty, "base_policy");
    }

    private static (bool IsValid, string? Value) ResolveOrigin(
        string? headerOrigin,
        string? requestedOrigin)
    {
        string? normalizedHeader = null;
        string? normalizedRequested = null;

        if (!string.IsNullOrWhiteSpace(headerOrigin) &&
            !ProtectedActionPolicy.TryNormalizeOrigin(headerOrigin, out normalizedHeader))
        {
            return (false, null);
        }

        if (!string.IsNullOrWhiteSpace(requestedOrigin) &&
            !ProtectedActionPolicy.TryNormalizeOrigin(requestedOrigin, out normalizedRequested))
        {
            return (false, null);
        }

        if (normalizedHeader != null &&
            normalizedRequested != null &&
            !string.Equals(normalizedHeader, normalizedRequested, StringComparison.OrdinalIgnoreCase))
        {
            return (false, null);
        }

        return (true, normalizedHeader ?? normalizedRequested);
    }

    private static bool IsOriginAllowed(
        Project project,
        ProtectedAction action,
        string? origin)
    {
        var configured = action.AllowedOrigins.Count > 0
            ? action.AllowedOrigins
            : project.AllowedDomains;

        if (configured.Count == 0)
            return true;

        if (string.IsNullOrWhiteSpace(origin))
            return false;

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
            return false;

        foreach (var allowed in configured)
        {
            if (!ProtectedActionPolicy.TryNormalizeOrigin(allowed, out var normalizedAllowed))
                continue;

            if (normalizedAllowed.Contains("://", StringComparison.Ordinal))
            {
                if (string.Equals(normalizedAllowed, origin, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else if (string.Equals(normalizedAllowed, originUri.Authority, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(normalizedAllowed, originUri.Host, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task LogFailureAsync(
        Project project,
        ProtectedAction action,
        CostShieldRequestContext context,
        string? origin,
        string? subject,
        string reason,
        CancellationToken ct)
    {
        await LogEventAsync(
            project,
            action,
            AuthEventType.CostShieldChallengeFailed,
            success: false,
            context,
            origin,
            subject,
            reason,
            ct);
    }

    private async Task LogReplayAsync(
        Project project,
        ProtectedAction action,
        CostShieldRequestContext context,
        string clientContextHash,
        string ipHash,
        string? subjectHash,
        CancellationToken ct)
    {
        _db.AuthEvents.Add(CreateEvent(
            project,
            action,
            AuthEventType.CostShieldReplayBlocked,
            success: false,
            context,
            clientContextHash,
            ipHash,
            subjectHash,
            reason: "challenge_replayed"));
        await _db.SaveChangesAsync(ct);
    }

    private async Task LogEventAsync(
        Project project,
        ProtectedAction action,
        AuthEventType eventType,
        bool success,
        CostShieldRequestContext context,
        string? origin,
        string? subject,
        string reason,
        CancellationToken ct)
    {
        var clientContextHash = _contextHasher.HashContext(
            project.Id,
            context.IpAddress,
            context.UserAgent,
            subject);
        _db.AuthEvents.Add(CreateEvent(
            project,
            action,
            eventType,
            success,
            context,
            clientContextHash,
            _contextHasher.HashIp(context.IpAddress),
            string.IsNullOrWhiteSpace(subject) ? null : _contextHasher.HashSubject(subject),
            reason,
            metadata: new { origin }));
        await _db.SaveChangesAsync(ct);
    }

    private static AuthEvent CreateEvent(
        Project project,
        ProtectedAction action,
        AuthEventType eventType,
        bool success,
        CostShieldRequestContext context,
        string clientContextHash,
        string ipHash,
        string? subjectHash,
        string reason,
        object? metadata = null)
    {
        return new AuthEvent
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            ProtectedActionId = action.Id,
            EventType = eventType,
            Environment = action.Environment,
            CreatedAt = DateTime.UtcNow,
            ClientIp = null,
            IpAddressHash = ipHash,
            ClientContextHash = clientContextHash,
            SubjectHash = subjectHash,
            VerificationMethod = eventType is AuthEventType.CostShieldChallengeCompleted
                or AuthEventType.CostShieldAuthorizationIssued
                ? "pow"
                : null,
            Success = success,
            Reason = reason,
            MetadataJson = metadata == null
                ? null
                : JsonSerializer.Serialize(metadata)
        };
    }

    private static (string Code, string Message)? ValidateRequestInput(
        string? action,
        string? environment,
        string? subject,
        Dictionary<string, string>? metadata)
    {
        if (string.IsNullOrWhiteSpace(action) || action.Trim().Length > 100)
            return ("invalid_action", "Action is required and must be 100 characters or less.");

        var normalizedEnvironment = environment?.Trim().ToUpperInvariant();
        if (normalizedEnvironment is not ("TEST" or "LIVE"))
            return ("invalid_environment", "Environment must be TEST or LIVE.");

        if (subject?.Length > 256)
            return ("invalid_subject", "Subject must be 256 characters or less.");

        if (metadata?.Any(pair =>
                string.IsNullOrWhiteSpace(pair.Key) ||
                pair.Key.Length > 64 ||
                pair.Value == null ||
                pair.Value.Length > 256) == true)
        {
            return ("invalid_client_metadata", "Client metadata is too large.");
        }

        if (metadata is { Count: > 20 } ||
            metadata?.Values.Sum(value => value?.Length ?? 0) > 2_048)
            return ("invalid_client_metadata", "Client metadata is too large.");

        return null;
    }

    private static bool IsChallengeIdValid(string challengeId)
        => challengeId.Length == 32 &&
           challengeId.All(character =>
               character is >= '0' and <= '9' ||
               character is >= 'a' and <= 'f');

    private static bool IsChallengeSignatureValid(string? signature)
        => signature is { Length: 64 } &&
           signature.All(character =>
               character is >= '0' and <= '9' ||
               character is >= 'a' and <= 'f' ||
               character is >= 'A' and <= 'F');

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        var message = exception.InnerException?.Message ?? exception.Message;
        return message.Contains("UNIQUE constraint", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase);
    }
}

public static class CostShieldChallengePayload
{
    public static string Build(
        Guid projectId,
        Guid actionId,
        string environment,
        string action,
        string? origin,
        string clientContextHash,
        string? subjectHash,
        string challengeId,
        int difficultyBits,
        long expiresAtUnix,
        int configurationVersion)
    {
        return string.Join(
            '\n',
            "liveauth-costshield-v1",
            projectId.ToString("N"),
            actionId.ToString("N"),
            environment,
            action,
            Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(origin ?? string.Empty)),
            clientContextHash,
            subjectHash ?? string.Empty,
            challengeId,
            difficultyBits.ToString(),
            expiresAtUnix.ToString(),
            configurationVersion.ToString());
    }
}

public sealed record CostShieldRequestContext(
    string? IpAddress,
    string? UserAgent,
    string? Origin);

public sealed record CostShieldDifficultyDecision(
    int Bits,
    string Reason);

public sealed record CostShieldRateState(
    int Count,
    int Limit,
    bool Exceeded,
    bool NearLimit,
    int RetryAfterSeconds,
    string Reason);

public enum CostShieldFlowStatus
{
    Ok,
    BadRequest,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    RateLimited
}

public sealed record CostShieldFlowError(
    CostShieldFlowStatus Status,
    string Code,
    string Message,
    int? RetryAfterSeconds = null);

public sealed record CostShieldFlowResult<T>(
    CostShieldFlowStatus Status,
    T? Value,
    CostShieldFlowError? Error)
{
    public static CostShieldFlowResult<T> Ok(T value)
        => new(CostShieldFlowStatus.Ok, value, null);

    public static CostShieldFlowResult<T> BadRequest(string code, string message)
        => ErrorResult(CostShieldFlowStatus.BadRequest, code, message);

    public static CostShieldFlowResult<T> Unauthorized(string code, string message)
        => ErrorResult(CostShieldFlowStatus.Unauthorized, code, message);

    public static CostShieldFlowResult<T> Forbidden(string code, string message)
        => ErrorResult(CostShieldFlowStatus.Forbidden, code, message);

    public static CostShieldFlowResult<T> Conflict(string code, string message)
        => ErrorResult(CostShieldFlowStatus.Conflict, code, message);

    public static CostShieldFlowResult<T> RateLimited(
        string code,
        string message,
        int retryAfterSeconds)
        => new(
            CostShieldFlowStatus.RateLimited,
            default,
            new CostShieldFlowError(
                CostShieldFlowStatus.RateLimited,
                code,
                message,
                retryAfterSeconds));

    public static CostShieldFlowResult<T> FromError(CostShieldFlowError error)
        => new(error.Status, default, error);

    private static CostShieldFlowResult<T> ErrorResult(
        CostShieldFlowStatus status,
        string code,
        string message)
        => new(status, default, new CostShieldFlowError(status, code, message));
}
