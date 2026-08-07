using System.Collections.Concurrent;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Services.Meter;

public interface IMeterAllowanceService
{
    Task<bool> TryConsumeFreeRequestAsync(Guid projectId, string environment, string callerKey, Guid? routeRuleId,
        long routeAllowance, long projectAllowance, CancellationToken ct);
}

public sealed class MeterAllowanceService : IMeterAllowanceService
{
    private readonly LiveAuthDbContext _db;
    public MeterAllowanceService(LiveAuthDbContext db) => _db = db;

    public async Task<bool> TryConsumeFreeRequestAsync(Guid projectId, string environment, string callerKey,
        Guid? routeRuleId, long routeAllowance, long projectAllowance, CancellationToken ct)
    {
        if (routeRuleId.HasValue && routeAllowance > 0 &&
            await TryConsumeScopeAsync(projectId, environment, callerKey, $"route:{routeRuleId:N}", routeAllowance, ct))
            return true;
        return projectAllowance > 0 &&
            await TryConsumeScopeAsync(projectId, environment, callerKey, "project", projectAllowance, ct);
    }

    private async Task<bool> TryConsumeScopeAsync(Guid projectId, string environment, string callerKey,
        string scopeKey, long allowance, CancellationToken ct)
    {
        var month = DateTime.UtcNow.ToString("yyyy-MM");
        for (var attempt = 0; attempt < 4; attempt++)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var counter = await _db.MeterAllowanceCounters.SingleOrDefaultAsync(x =>
                x.ProjectId == projectId && x.Environment == environment && x.MonthUtc == month &&
                x.CallerKey == callerKey && x.ScopeKey == scopeKey, ct);
            if (counter == null)
            {
                counter = new MeterAllowanceCounter
                {
                    ProjectId = projectId, Environment = environment, MonthUtc = month,
                    CallerKey = callerKey, ScopeKey = scopeKey, Used = 1
                };
                _db.MeterAllowanceCounters.Add(counter);
                try
                {
                    await _db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                    return true;
                }
                catch (DbUpdateException)
                {
                    await transaction.RollbackAsync(ct);
                    _db.Entry(counter).State = EntityState.Detached;
                    continue;
                }
            }

            if (counter.Used >= allowance)
            {
                await transaction.RollbackAsync(ct);
                return false;
            }
            counter.Used++;
            counter.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        }
        return false;
    }
}

public sealed record MeterChallengeResult(MeterPaymentChallenge Challenge, bool Reused);
public sealed record MeterAuthorizationResult(bool Authorized, MeterPaymentChallenge? Challenge, string? Error, bool NewlySettled = false);

public interface IMeterPaymentService
{
    Task<MeterChallengeResult> CreateOrReuseChallengeAsync(Project project, MeterProjectSettings settings,
        MeterRouteDecision route, string method, string path, string callerKey, string correlationId,
        string? requestBodyHash, CancellationToken ct);
    Task<MeterAuthorizationResult> AuthorizeAsync(MeterProjectSettings settings, MeterRouteDecision route,
        string method, string path, string? requestBodyHash, string authorizationHeader, CancellationToken ct);
}

public sealed class MeterPaymentService : IMeterPaymentService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ChallengeLocks = new();
    private readonly LiveAuthDbContext _db;
    private readonly ILightningInvoiceProviderFactory _providers;
    private readonly IMeterCredentialService _credentials;
    private readonly IConfiguration _configuration;

    public MeterPaymentService(LiveAuthDbContext db, ILightningInvoiceProviderFactory providers,
        IMeterCredentialService credentials, IConfiguration configuration)
    {
        _db = db; _providers = providers; _credentials = credentials; _configuration = configuration;
    }

    public async Task<MeterChallengeResult> CreateOrReuseChallengeAsync(Project project, MeterProjectSettings settings,
        MeterRouteDecision route, string method, string path, string callerKey, string correlationId,
        string? requestBodyHash, CancellationToken ct)
    {
        if (settings.LightningConnection == null)
            throw new MeterConfigurationException("lightning_not_configured", "A merchant Lightning connection is required.");
        var lifetime = TimeSpan.FromSeconds(Math.Clamp(route.Rule?.CredentialLifetimeSeconds ?? 3600, 60, 86400));
        var maximumUses = Math.Clamp(route.Rule?.MaximumCredentialUses ?? 1, 1, 10_000);
        var bucketSeconds = Math.Clamp(_configuration.GetValue("Meter:ChallengeIdempotencyWindowSeconds", 30), 5, 300);
        var bucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / bucketSeconds;
        var challengeKey = HashKey(string.Join('\n', project.Id, settings.Environment, callerKey,
            method.ToUpperInvariant(), path, route.Rule?.Id.ToString("N") ?? "default", requestBodyHash ?? "", bucket));
        var gate = ChallengeLocks.GetOrAdd(challengeKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var now = DateTime.UtcNow;
            var existing = await _db.MeterPaymentChallenges.AsNoTracking().FirstOrDefaultAsync(x =>
                x.ChallengeKey == challengeKey && x.Status == MeterChallengeStatuses.Pending && x.ExpiresAt > now, ct);
            if (existing != null) return new(existing, true);

            var provider = _providers.Get(settings.LightningConnection.ProviderType);
            var invoiceExpiry = TimeSpan.FromMinutes(Math.Clamp(_configuration.GetValue("Meter:InvoiceExpiryMinutes", 10), 2, 60));
            var invoice = await provider.CreateInvoiceAsync(settings.LightningConnection, route.PriceSats,
                $"LiveAuth Meter {method.ToUpperInvariant()} {route.NormalizedRoute}", invoiceExpiry, ct);
            var challenge = new MeterPaymentChallenge
            {
                ProjectId = project.Id, Environment = settings.Environment, RouteRuleId = route.Rule?.Id,
                HttpMethod = method.ToUpperInvariant(), RequestedPath = path, NormalizedRoute = route.NormalizedRoute,
                PriceSats = route.PriceSats, PaymentHash = invoice.PaymentHash, Invoice = invoice.Bolt11,
                MerchantLightningProviderId = settings.LightningConnection.Id, CreatedAt = now,
                ExpiresAt = invoice.ExpiresAt, CredentialExpiresAt = now.Add(lifetime), MaximumUses = maximumUses,
                RemainingUses = maximumUses, Status = MeterChallengeStatuses.Pending,
                RequestCorrelationId = correlationId, ChallengeKey = challengeKey,
                CredentialNonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant(),
                RequestBodyHash = route.Rule?.BindRequestBody == true ? requestBodyHash : null
            };
            challenge.Macaroon = _credentials.Issue(challenge);
            _db.MeterPaymentChallenges.Add(challenge);
            await _db.SaveChangesAsync(ct);
            return new(challenge, false);
        }
        finally
        {
            gate.Release();
            if (gate.CurrentCount == 1) ChallengeLocks.TryRemove(challengeKey, out _);
        }
    }

    public async Task<MeterAuthorizationResult> AuthorizeAsync(MeterProjectSettings settings, MeterRouteDecision route,
        string method, string path, string? requestBodyHash, string authorizationHeader, CancellationToken ct)
    {
        if (!_credentials.TryParseAuthorization(authorizationHeader, out var authorization) || authorization == null)
            return new(false, null, "invalid_authorization");
        if (!_credentials.TryValidate(authorization.Macaroon, out var payload, out var error) || payload == null)
            return new(false, null, error);
        if (payload.ProjectId != settings.ProjectId || payload.Environment != settings.Environment ||
            payload.RouteRuleId != route.Rule?.Id || payload.Method != method.ToUpperInvariant() ||
            payload.PathPattern != route.NormalizedRoute || payload.PriceSats != route.PriceSats)
            return new(false, null, "credential_caveat_mismatch");
        if (payload.RequestBodyHash != null && !string.Equals(payload.RequestBodyHash, requestBodyHash, StringComparison.Ordinal))
            return new(false, null, "request_body_mismatch");
        if (!_credentials.PreimageMatches(authorization.Preimage, payload.PaymentHash))
            return new(false, null, "invalid_preimage");

        var now = DateTime.UtcNow;
        var challenge = await _db.MeterPaymentChallenges
            .Include(x => x.MerchantLightningProvider)
            .SingleOrDefaultAsync(x => x.Id == payload.ChallengeId && x.ProjectId == settings.ProjectId, ct);
        if (challenge == null || challenge.CredentialNonce != payload.Nonce || challenge.PaymentHash != payload.PaymentHash)
            return new(false, null, "credential_not_found");
        if ((challenge.PaidAt == null && challenge.ExpiresAt <= now) || challenge.CredentialExpiresAt <= now)
            return new(false, challenge, "credential_expired");
        if (challenge.Status is MeterChallengeStatuses.Exhausted or MeterChallengeStatuses.Expired || challenge.RemainingUses <= 0)
            return new(false, challenge, "credential_exhausted");

        var newlySettled = false;
        if (challenge.PaidAt == null)
        {
            var provider = _providers.Get(challenge.MerchantLightningProvider.ProviderType);
            var status = await provider.LookupInvoiceAsync(challenge.MerchantLightningProvider, challenge.PaymentHash, ct);
            if (!status.Settled) return new(false, challenge, "payment_not_settled");
            newlySettled = await _db.MeterPaymentChallenges
                .Where(x => x.Id == challenge.Id && x.PaidAt == null)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(x => x.PaidAt, status.SettledAt ?? now)
                    .SetProperty(x => x.Status, MeterChallengeStatuses.Paid), ct) == 1;
            challenge.PaidAt = status.SettledAt ?? now;
        }

        // A single conditional UPDATE is the replay/usage gate. Concurrent callers
        // cannot take the remaining count below zero.
        var updated = await _db.MeterPaymentChallenges
            .Where(x => x.Id == challenge.Id && x.RemainingUses > 0 &&
                x.CredentialExpiresAt > now && x.Status != MeterChallengeStatuses.Exhausted)
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.RemainingUses, x => x.RemainingUses - 1)
                .SetProperty(x => x.Status, x => x.RemainingUses == 1
                    ? MeterChallengeStatuses.Exhausted : MeterChallengeStatuses.Paid), ct);
        if (updated != 1) return new(false, challenge, "credential_exhausted");
        challenge.PaidAt ??= now;
        challenge.RemainingUses--;
        challenge.Status = challenge.RemainingUses == 0 ? MeterChallengeStatuses.Exhausted : MeterChallengeStatuses.Paid;
        return new(true, challenge, null, newlySettled);
    }

    private string HashKey(string value)
    {
        var secret = _configuration["Meter:ChallengeHmacKey"] ?? _configuration["LiveAuth:PowHmacSecret"]
            ?? throw new InvalidOperationException("Meter challenge HMAC key is not configured.");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}

public sealed class MeterConfigurationException : Exception
{
    public MeterConfigurationException(string code, string message) : base(message) => Code = code;
    public string Code { get; }
}
