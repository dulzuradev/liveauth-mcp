using FluentAssertions;
using LiveAuthCore.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LiveAuthCore.Tests.Services;

public class L402ServiceTests
{
    [Fact]
    public async Task IssuedToken_DefaultsToSingleUseAllowance()
    {
        var service = CreateService();
        var token = await service.IssueTokenAsync("payment-hash-test");

        token.Should().NotBeNullOrWhiteSpace();
        service.IsTokenValid(token).Should().BeTrue();
        service.TryConsumeToken(token).Should().BeTrue();
        service.IsTokenValid(token).Should().BeFalse();
        service.TryConsumeToken(token).Should().BeFalse();
    }

    [Fact]
    public async Task IssuedToken_RespectsConfiguredAllowance()
    {
        var service = CreateService(new Dictionary<string, string?>
        {
            ["L402:TokenCallAllowance"] = "2"
        });
        var token = await service.IssueTokenAsync("payment-hash-test");

        service.TryConsumeToken(token).Should().BeTrue();
        service.IsTokenValid(token).Should().BeTrue();
        service.TryConsumeToken(token).Should().BeTrue();
        service.IsTokenValid(token).Should().BeFalse();
    }

    private static L402Service CreateService(Dictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Lnd:UseMock"] = "true",
            ["L402:TokenTtlMinutes"] = "60"
        };

        if (overrides != null)
        {
            foreach (var item in overrides)
                settings[item.Key] = item.Value;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        return new L402Service(
            new LightningService(configuration),
            new MemoryCache(new MemoryCacheOptions()),
            configuration);
    }
}
