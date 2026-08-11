using System.Collections.Concurrent;
using System.Security.Claims;
using LiveAuthCore.Bitcoin.Configuration;
using Microsoft.Extensions.Options;

namespace LiveAuthCore.Bitcoin.Services;

public interface IBitcoinGatewayRateLimiter
{
    void Acquire(ClaimsPrincipal caller, bool broadcast);
}

public sealed class BitcoinGatewayRateLimiter : IBitcoinGatewayRateLimiter
{
    private readonly ConcurrentDictionary<string, Window> _windows = new();
    private readonly IOptionsMonitor<BitcoinGatewayOptions> _options;

    public BitcoinGatewayRateLimiter(IOptionsMonitor<BitcoinGatewayOptions> options) => _options = options;

    public void Acquire(ClaimsPrincipal caller, bool broadcast)
    {
        var identity = caller.FindFirst("jti")?.Value ?? caller.FindFirst("projectId")?.Value ?? "unknown";
        var key = $"{identity}:{(broadcast ? "broadcast" : "read")}";
        var limit = broadcast
            ? Math.Clamp(_options.CurrentValue.BroadcastRateLimitPerMinute, 1, 1_000)
            : Math.Clamp(_options.CurrentValue.ReadRateLimitPerMinute, 1, 10_000);
        var now = DateTime.UtcNow;
        if (_windows.Count > 10_000)
        {
            foreach (var stale in _windows.Where(item => now - item.Value.StartedAt >= TimeSpan.FromMinutes(2)))
                _windows.TryRemove(stale.Key, out _);
        }
        var window = _windows.GetOrAdd(key, _ => new Window(now));
        lock (window)
        {
            if (now - window.StartedAt >= TimeSpan.FromMinutes(1))
            {
                window.StartedAt = now;
                window.Count = 0;
            }
            if (window.Count >= limit)
            {
                var retryAfter = Math.Max(1, (int)Math.Ceiling((window.StartedAt.AddMinutes(1) - now).TotalSeconds));
                throw new BitcoinGatewayException(BitcoinErrorCodes.RateLimited,
                    "Bitcoin Gateway rate limit exceeded.", true,
                    StatusCodes.Status429TooManyRequests, retryAfter);
            }
            window.Count++;
        }
    }

    private sealed class Window(DateTime startedAt)
    {
        public DateTime StartedAt { get; set; } = startedAt;
        public int Count { get; set; }
    }
}
