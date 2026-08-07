using System.Security.Cryptography;
using FluentAssertions;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Services.Meter;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace LiveAuthCore.Tests.Services.Meter;

public sealed class MeterCoreTests
{
    private static readonly IConfiguration Configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LiveAuth:PowHmacSecret"] = "meter-tests-pow-secret-at-least-32-bytes",
            ["Jwt:SigningKey"] = "meter-tests-jwt-secret-at-least-32-bytes",
            ["Meter:EncryptionKey"] = Convert.ToBase64String(Enumerable.Range(1, 32).Select(x => (byte)x).ToArray())
        }).Build();

    [Fact]
    public void Route_matching_is_deterministic_and_prefers_priority_then_specificity()
    {
        var matcher = new MeterRouteMatcher();
        var settings = new MeterProjectSettings { UnmatchedRouteBehavior = MeterUnmatchedRouteBehaviors.Block };
        var wildcard = new MeterRouteRule { Id = Guid.Parse("00000000-0000-0000-0000-000000000002"), HttpMethod = "GET", PathPattern = "/weather/*", PriceSats = 5, Priority = 1 };
        var literal = new MeterRouteRule { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), HttpMethod = "GET", PathPattern = "/weather/seattle", PriceSats = 2, Priority = 1 };
        matcher.Match(settings, new[] { wildcard, literal }, "GET", "/weather/seattle").Rule.Should().Be(literal);
        wildcard.Priority = 2;
        matcher.Match(settings, new[] { wildcard, literal }, "GET", "/weather/seattle").Rule.Should().Be(wildcard);
    }

    [Theory]
    [InlineData("FREE", false, 0)]
    [InlineData("BLOCK", true, 0)]
    [InlineData("DEFAULT_PRICE", false, 21)]
    public void Default_route_behavior_selects_expected_policy(string behavior, bool blocked, long price)
    {
        var result = new MeterRouteMatcher().Match(new MeterProjectSettings
        { UnmatchedRouteBehavior = behavior, DefaultPriceSats = 21 }, Array.Empty<MeterRouteRule>(), "POST", "/unknown");
        result.IsBlocked.Should().Be(blocked);
        result.PriceSats.Should().Be(price);
    }

    [Theory]
    [InlineData("/weather/*", null)]
    [InlineData("/users/:id", null)]
    [InlineData("/a/*/b", "wildcard")]
    [InlineData("relative", "begin")]
    public void Route_pattern_validation_documents_supported_syntax(string pattern, string? errorFragment)
    {
        var error = new MeterRouteMatcher().ValidatePattern(pattern);
        if (errorFragment == null) error.Should().BeNull(); else error.Should().Contain(errorFragment);
    }

    [Fact]
    public void Credential_enforces_signature_expiry_and_conventional_header_parsing()
    {
        var service = new MeterCredentialService(Configuration);
        var preimage = RandomNumberGenerator.GetBytes(32);
        var challenge = Challenge(preimage, DateTime.UtcNow.AddMinutes(5));
        challenge.Macaroon = service.Issue(challenge);
        service.TryValidate(challenge.Macaroon, out var payload, out _).Should().BeTrue();
        payload!.ProjectId.Should().Be(challenge.ProjectId);
        service.PreimageMatches(Convert.ToHexString(preimage), challenge.PaymentHash).Should().BeTrue();
        service.TryParseAuthorization($"L402 {challenge.Macaroon}:{Convert.ToHexString(preimage)}", out var auth).Should().BeTrue();
        auth!.Macaroon.Should().Be(challenge.Macaroon);
        service.TryValidate(challenge.Macaroon + "x", out _, out _).Should().BeFalse();

        var expired = Challenge(preimage, DateTime.UtcNow.AddSeconds(-1));
        service.TryValidate(service.Issue(expired), out _, out var error).Should().BeFalse();
        error.Should().Be("credential_expired");
    }

    [Fact]
    public void Receipt_canonicalization_is_stable_and_signature_detects_tampering()
    {
        var service = new MeterReceiptService(Configuration);
        var receipt = service.Create(new MeterReceiptInput(Guid.NewGuid(), Guid.NewGuid(), "TEST", "GET", "/weather/*",
            DateTime.UnixEpoch, DateTime.UnixEpoch.AddSeconds(1), 5, new string('a', 64), Guid.NewGuid(), "request-1", 200, 30, 20));
        service.Verify(receipt).Should().BeTrue();
        receipt.CanonicalPayload.Should().StartWith("{\"amountPaidSats\":5,\"authorizationTimestamp\"");
        receipt.CanonicalPayload += " ";
        service.Verify(receipt).Should().BeFalse();
    }

    [Fact]
    public void Merchant_secrets_are_encrypted_and_authenticated()
    {
        var service = new MeterSecretProtector(Configuration, new TestEnvironment());
        var encrypted = service.Protect("invoice-macaroon");
        encrypted.Should().NotContain("invoice-macaroon");
        service.Unprotect(encrypted).Should().Be("invoice-macaroon");
        var parts = encrypted.Split('.');
        parts[2] = (parts[2][0] == 'A' ? "B" : "A") + parts[2][1..];
        var tampered = string.Join('.', parts);
        var act = () => service.Unprotect(tampered);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public async Task Ssrf_guard_blocks_loopback_metadata_and_insecure_live_origins()
    {
        var guard = new MeterSsrfGuard();
        await FluentActions.Invoking(() => guard.ValidateAndResolveAsync("http://127.0.0.1:4010", false, false, default))
            .Should().ThrowAsync<MeterSecurityException>();
        await FluentActions.Invoking(() => guard.ValidateAndResolveAsync("http://169.254.169.254/latest", false, true, default))
            .Should().ThrowAsync<MeterSecurityException>();
        await FluentActions.Invoking(() => guard.ValidateAndResolveAsync("http://example.com", true, false, default))
            .Should().ThrowAsync<MeterSecurityException>().WithMessage("*HTTPS*");
    }

    private static MeterPaymentChallenge Challenge(byte[] preimage, DateTime expires) => new()
    {
        Id = Guid.NewGuid(), ProjectId = Guid.NewGuid(), Environment = "TEST", RouteRuleId = Guid.NewGuid(),
        HttpMethod = "POST", NormalizedRoute = "/research", PriceSats = 500,
        PaymentHash = Convert.ToHexString(SHA256.HashData(preimage)).ToLowerInvariant(),
        CredentialExpiresAt = expires, MaximumUses = 1, CredentialNonce = Guid.NewGuid().ToString("N")
    };

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
