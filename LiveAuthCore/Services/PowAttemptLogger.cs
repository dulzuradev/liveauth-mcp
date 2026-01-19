using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace LiveAuthCore.Services;

public sealed class PowAttemptLogger
{
    private readonly IDistributedCache _cache;

    public PowAttemptLogger(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task RecordAsync(
        Guid projectId,
        long solveMs,
        bool success,
        CancellationToken ct)
    {
        var key = $"pow:attempts:{projectId}";
        var raw = await _cache.GetStringAsync(key, ct);

        PowAttemptStats stats = raw != null
            ? JsonSerializer.Deserialize<PowAttemptStats>(raw)!
            : new PowAttemptStats
            {
                Attempts = 0,
                Successes = 0,
                Failures = 0,
                AvgSolveMs = 0,
                LastSeenUnix = 0
            };

        var attempts = stats.Attempts + 1;
        var successes = stats.Successes + (success ? 1 : 0);
        var failures = stats.Failures + (success ? 0 : 1);

        var avgSolve =
            stats.Attempts == 0
                ? solveMs
                : (stats.AvgSolveMs * stats.Attempts + solveMs) / attempts;

        var updated = stats with
        {
            Attempts = attempts,
            Successes = successes,
            Failures = failures,
            AvgSolveMs = avgSolve,
            LastSeenUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        await _cache.SetStringAsync(
            key,
            JsonSerializer.Serialize(updated),
            new DistributedCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(15)
            },
            ct
        );
    }

    public async Task<PowAttemptStats?> GetAsync(
        Guid projectId,
        CancellationToken ct)
    {
        var raw = await _cache.GetStringAsync(
            $"pow:attempts:{projectId}",
            ct
        );

        return raw == null
            ? null
            : JsonSerializer.Deserialize<PowAttemptStats>(raw);
    }
}
