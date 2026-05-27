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

    [Fact]
    public async Task IssuedToken_ReportsConfiguredTtlAndRemainingCalls()
    {
        var service = CreateService(new Dictionary<string, string?>
        {
            ["L402:TokenCallAllowance"] = "3",
            ["L402:TokenTtlMinutes"] = "2"
        });

        var token = await service.IssueTokenAsync("payment-hash-test");

        service.TokenCallAllowance.Should().Be(3);
        service.TokenTtlSeconds.Should().Be(120);
        service.GetRemainingTokenCalls(token).Should().Be(3);

        service.TryConsumeToken(token).Should().BeTrue();
        service.GetRemainingTokenCalls(token).Should().Be(2);
    }

    [Fact]
    public async Task TryConsumeToken_WithProjectId_RejectsWrongProjectWithoutConsuming()
    {
        var projectId = Guid.NewGuid();
        var wrongProjectId = Guid.NewGuid();
        var service = CreateService();

        service.BindInvoiceToProject("payment-hash-test", projectId);
        var token = await service.IssueTokenAsync("payment-hash-test");

        service.IsTokenValid(token, projectId).Should().BeTrue();
        service.TryConsumeToken(token, wrongProjectId).Should().BeFalse();
        service.IsTokenValid(token, projectId).Should().BeTrue();
        service.GetRemainingTokenCalls(token).Should().Be(1);

        service.TryConsumeToken(token, projectId).Should().BeTrue();
        service.IsTokenValid(token, projectId).Should().BeFalse();
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
