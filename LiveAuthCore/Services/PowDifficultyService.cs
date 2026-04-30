using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using LiveAuthCore.Data.Entities;

namespace LiveAuthCore.Services;

public sealed class PowDifficultyService
{
    private readonly IDistributedCache _cache;

    public PowDifficultyService(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<int> GetDifficultyAsync(
        Project project,
        CancellationToken ct)
    {
        // Demo project gets easy difficulty for testing
        if (project.Id == Guid.Parse("B842CAE1-E06E-480F-BE76-A64A75E0F871"))
            return 10;

        var baseBits = project.Plan == "free" ? 17 : 19;

        var stats = await GetStatsAsync(project.Id, ct);

        int adjustment = 0;

        if (stats != null)
        {
            if (stats.AvgSolveMs < 200) adjustment += 2;
            else if (stats.AvgSolveMs < 400) adjustment += 1;
            else if (stats.AvgSolveMs > 1500) adjustment -= 1;

            if (stats.Attempts > 50) adjustment += 2;
            if (stats.Failures > 10) adjustment += 2;
        }

        return Math.Clamp(baseBits + adjustment, 16, 24);
    }

    /* ============================================================
     * Stats
     * ============================================================ */

    private async Task<PowStats?> GetStatsAsync(Guid projectId, CancellationToken ct)
    {
        var key = $"pow:stats:{projectId}";
        var raw = await _cache.GetStringAsync(key, ct);

        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            return JsonSerializer.Deserialize<PowStats>(raw);
        }
        catch
        {
            // Corrupt cache entry → ignore and reset naturally
            return null;
        }
    }

    public async Task RecordResultAsync(
        Guid projectId,
        long solveMs,
        bool success,
        CancellationToken ct)
    {
        var key = $"pow:stats:{projectId}";
        var lockKey = $"pow:stats:lock:{projectId}";

        // Acquire distributed lock to prevent concurrent read-modify-write race.
        // Without this, two near-simultaneous completions can overwrite each other's stats.
        var lockAcquired = await AcquireLockAsync(lockKey, TimeSpan.FromSeconds(5), ct);
        if (!lockAcquired)
        {
            // Another process is updating; skip this record (better than blocking).
            return;
        }

        try
        {
            var existing = await GetStatsAsync(projectId, ct);

            var attempts = (existing?.Attempts ?? 0) + 1;
            var failures = (existing?.Failures ?? 0) + (success ? 0 : 1);

            var avgSolve =
                existing == null
                    ? solveMs
                    : (existing.AvgSolveMs * existing.Attempts + solveMs) / attempts;

            var updated = new PowStats(
                AvgSolveMs: avgSolve,
                Attempts: attempts,
                Failures: failures
            );

            await _cache.SetStringAsync(
                key,
                JsonSerializer.Serialize(updated),
                new DistributedCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromMinutes(10)
                },
                ct
            );
        }
        finally
        {
            await ReleaseLockAsync(lockKey);
        }
    }

    private async Task<bool> AcquireLockAsync(string key, TimeSpan ttl, CancellationToken ct)
    {
        try
        {
            var lockValue = Guid.NewGuid().ToString();
            await _cache.SetStringAsync(key, lockValue, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl
            }, ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private Task ReleaseLockAsync(string key)
        // Fire-and-forget release; lock auto-expires via TTL so no strict cleanup needed.
        => _cache.RemoveAsync(key);

    /* ============================================================
     * DTO
     * ============================================================ */

    private sealed record PowStats(
        double AvgSolveMs,
        int Attempts,
        int Failures
    );
}
