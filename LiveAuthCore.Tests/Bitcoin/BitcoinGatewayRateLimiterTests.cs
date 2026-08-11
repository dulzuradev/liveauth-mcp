using System.Security.Claims;
using LiveAuthCore.Bitcoin;
using LiveAuthCore.Bitcoin.Configuration;
using LiveAuthCore.Bitcoin.Services;
using Xunit;

namespace LiveAuthCore.Tests.Bitcoin;

public sealed class BitcoinGatewayRateLimiterTests
{
    [Fact]
    public void Broadcast_has_a_separate_lower_per_client_limit()
    {
        var options = new StaticOptionsMonitor<BitcoinGatewayOptions>(new BitcoinGatewayOptions
        {
            ReadRateLimitPerMinute = 10,
            BroadcastRateLimitPerMinute = 1
        });
        var limiter = new BitcoinGatewayRateLimiter(options);
        var caller = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("jti", "client-a") }));

        limiter.Acquire(caller, false);
        limiter.Acquire(caller, false);
        limiter.Acquire(caller, true);
        var error = Assert.Throws<BitcoinGatewayException>(() => limiter.Acquire(caller, true));

        Assert.Equal(BitcoinErrorCodes.RateLimited, error.Code);
        Assert.True(error.Retryable);
        Assert.Equal(StatusCodes.Status429TooManyRequests, error.StatusCode);
        Assert.True(error.RetryAfterSeconds > 0);
    }
}
