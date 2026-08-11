using LiveAuthCore.Bitcoin.Configuration;
using Microsoft.Extensions.Options;

namespace LiveAuthCore.Bitcoin.Rpc;

public sealed class BitcoinRpcCircuitBreaker
{
    private readonly object _gate = new();
    private readonly IOptionsMonitor<BitcoinGatewayOptions> _options;
    private int _consecutiveFailures;
    private DateTime _openUntilUtc;
    private long _openEvents;

    public long OpenEvents => Interlocked.Read(ref _openEvents);

    public BitcoinRpcCircuitBreaker(IOptionsMonitor<BitcoinGatewayOptions> options) => _options = options;

    public void ThrowIfOpen()
    {
        lock (_gate)
        {
            if (_openUntilUtc <= DateTime.UtcNow) return;
            var retryAfter = Math.Max(1, (int)Math.Ceiling((_openUntilUtc - DateTime.UtcNow).TotalSeconds));
            throw new BitcoinGatewayException(BitcoinErrorCodes.NodeUnavailable,
                "The LiveAuth Bitcoin node circuit is temporarily open.", true,
                StatusCodes.Status503ServiceUnavailable, retryAfter);
        }
    }

    public void RecordSuccess(long elapsedMilliseconds)
    {
        lock (_gate)
        {
            if (elapsedMilliseconds > Math.Max(1, _options.CurrentValue.CircuitBreakerThresholdMs))
            {
                RecordFailureUnsafe();
                return;
            }
            _consecutiveFailures = 0;
            _openUntilUtc = DateTime.MinValue;
        }
    }

    public void RecordFailure()
    {
        lock (_gate) RecordFailureUnsafe();
    }

    private void RecordFailureUnsafe()
    {
        _consecutiveFailures++;
        var options = _options.CurrentValue;
        if (_consecutiveFailures < Math.Max(1, options.CircuitBreakerFailureThreshold)) return;
        _openUntilUtc = DateTime.UtcNow.AddSeconds(Math.Clamp(options.CircuitBreakerBreakSeconds, 1, 300));
        Interlocked.Increment(ref _openEvents);
        _consecutiveFailures = 0;
    }
}
