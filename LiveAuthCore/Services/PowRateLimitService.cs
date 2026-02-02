using Microsoft.Extensions.Caching.Memory;

namespace LiveAuthCore.Services;

/// <summary>
/// In-memory rate limiter for PoW challenge generation.
/// Prevents hash grinding and DoS by limiting challenge requests per IP and per project.
/// </summary>
public sealed class PowRateLimitService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<PowRateLimitService> _logger;

    // Limits
    private const int MaxPerIpPerMinute = 10;
    private const int MaxPerProjectPerMinute = 100;

    public PowRateLimitService(IMemoryCache cache, ILogger<PowRateLimitService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Check if this IP or project has exceeded rate limits.
    /// Returns true if request is allowed, false if rate-limited.
    /// </summary>
    public bool TryAcquire(string ipAddress, Guid projectId)
    {
        var ipKey = $"ratelimit:ip:{ipAddress}";
        var projectKey = $"ratelimit:project:{projectId}";

        var now = DateTimeOffset.UtcNow;

        // Check IP limit
        if (!CheckAndIncrement(ipKey, MaxPerIpPerMinute, now))
        {
            _logger.LogWarning("Rate limit exceeded: IP {IpAddress} hit {Limit}/min", ipAddress, MaxPerIpPerMinute);
            return false;
        }

        // Check project limit
        if (!CheckAndIncrement(projectKey, MaxPerProjectPerMinute, now))
        {
            _logger.LogWarning("Rate limit exceeded: Project {ProjectId} hit {Limit}/min", projectId, MaxPerProjectPerMinute);
            return false;
        }

        return true;
    }

    private bool CheckAndIncrement(string key, int limit, DateTimeOffset now)
    {
        var bucket = _cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
            return new RateLimitBucket
            {
                Count = 0,
                WindowStart = now
            };
        });

        if (bucket == null)
        {
            // Should never happen, but handle gracefully
            return true;
        }

        // Reset window if expired
        if (now - bucket.WindowStart >= TimeSpan.FromMinutes(1))
        {
            bucket.Count = 0;
            bucket.WindowStart = now;
        }

        // Check limit
        if (bucket.Count >= limit)
        {
            return false;
        }

        // Increment
        bucket.Count++;
        return true;
    }

    private sealed class RateLimitBucket
    {
        public int Count { get; set; }
        public DateTimeOffset WindowStart { get; set; }
    }
}
