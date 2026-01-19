using Microsoft.Extensions.Caching.Distributed;
using System.Security.Cryptography;
using System.Text;

namespace LiveAuthCore.Services;

public sealed class PowReplayService
{
    private readonly IDistributedCache _cache;

    public PowReplayService(IDistributedCache cache)
    {
        _cache = cache;
    }

    /// <summary>
    /// Returns true if this challenge has NOT been used before and is now marked as used.
    /// Returns false if it was already used.
    /// </summary>
    public async Task<bool> TryMarkNonceUsedAsync(
        Guid projectId,
        string challengeHex,
        string clientNonce,
        long expiresAtUnix,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(challengeHex) ||
            string.IsNullOrWhiteSpace(clientNonce))
            return false;

        var key = BuildKey(projectId, challengeHex, clientNonce);

        // If already exists → replay
        var existing = await _cache.GetStringAsync(key, ct);
        if (existing != null)
            return false;

        // TTL until challenge expiry (minimum 1 second)
        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var ttlSeconds = Math.Max(1, expiresAtUnix - nowUnix);

        await _cache.SetStringAsync(
            key,
            "1",
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow =
                    TimeSpan.FromSeconds(ttlSeconds)
            },
            ct
        );

        return true;
    }

    private static string BuildKey(
        Guid projectId,
        string challengeHex,
        string clientNonce)
    {
        return $"pow:used:{projectId}:{challengeHex}:{clientNonce}";
    }
}