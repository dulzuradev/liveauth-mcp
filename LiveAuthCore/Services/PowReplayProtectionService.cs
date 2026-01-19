using Microsoft.Extensions.Caching.Distributed;

namespace LiveAuthCore.Services;

public sealed class PowReplayProtectionService
{
    private readonly IDistributedCache _cache;

    public PowReplayProtectionService(IDistributedCache cache)
    {
        _cache = cache;
    }

    /// <summary>
    /// Returns true if this (project, challenge, nonce) has NOT been used yet and we successfully marked it used.
    /// Returns false if it's already used (replay).
    ///
    /// NOTE: IDistributedCache does not guarantee atomic "set-if-not-exists".
    /// This is still very effective for v1, and becomes fully robust if you later swap in Redis with SET NX.
    /// </summary>
    public async Task<bool> TryMarkNonceUsedAsync(
        Guid projectId,
        string challengeHex,
        string clientNonce,
        long expiresAtUnix,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(challengeHex) || string.IsNullOrWhiteSpace(clientNonce))
            return false;

        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var ttlSeconds = expiresAtUnix - nowUnix;

        // If already expired, treat as not-usable
        if (ttlSeconds <= 0)
            return false;

        // Keep key short-ish, deterministic
        var key = $"pow:used:{projectId:N}:{challengeHex}:{clientNonce}";

        // best-effort replay check
        var existing = await _cache.GetStringAsync(key, ct);
        if (existing != null)
            return false;

        await _cache.SetStringAsync(
            key,
            "1",
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(ttlSeconds)
            },
            ct
        );

        return true;
    }
}