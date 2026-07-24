using FluentAssertions;
using LiveAuthCore.Models.CostShield;
using LiveAuthCore.Services.CostShield;
using Xunit;

namespace LiveAuthCore.Tests.Services.CostShield;

public sealed class ProtectedActionPolicyTests
{
    [Fact]
    public void Evaluate_ValidConfiguration_NormalizesSecuritySensitiveValues()
    {
        var request = ValidRequest();
        request.Environment = " live ";
        request.Name = " AI.Generate_Image ";
        request.AllowedOrigins = new List<string>
        {
            "HTTPS://App.Example.com/",
            "app.example.com",
            "https://app.example.com"
        };
        request.FailureBehavior = "lightningfallback";
        request.AllowLightningFallback = true;
        request.LightningFallbackMode = "always";

        var result = ProtectedActionPolicy.Evaluate(request);

        result.IsValid.Should().BeTrue();
        result.Normalized.Environment.Should().Be("LIVE");
        result.Normalized.Name.Should().Be("ai.generate_image");
        result.Normalized.AllowedOrigins.Should().Equal(
            "https://app.example.com",
            "app.example.com");
        result.Normalized.FailureBehavior.Should().Be("LightningFallback");
        result.Normalized.LightningFallbackMode.Should().Be("Always");
    }

    [Fact]
    public void Evaluate_InvalidDifficultyAndPartialAuthenticatedLimit_ReturnsFieldErrors()
    {
        var request = ValidRequest();
        request.BaseDifficulty = 22;
        request.SuspiciousDifficulty = 18;
        request.MaximumDifficulty = 20;
        request.AuthenticatedRequestLimit = 10;
        request.AuthenticatedLimitWindowSeconds = null;

        var result = ProtectedActionPolicy.Evaluate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainKey(nameof(request.BaseDifficulty));
        result.Errors.Should().ContainKey(nameof(request.AuthenticatedRequestLimit));
    }

    [Theory]
    [InlineData("https://example.com/path")]
    [InlineData("https://user@example.com")]
    [InlineData("*.example.com")]
    [InlineData("javascript:alert(1)")]
    public void Evaluate_UnsafeOrAmbiguousOrigin_ReturnsFieldError(string origin)
    {
        var request = ValidRequest();
        request.AllowedOrigins = new List<string> { origin };

        var result = ProtectedActionPolicy.Evaluate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainKey(nameof(request.AllowedOrigins));
    }

    [Fact]
    public void Evaluate_LightningFailureBehaviorWithoutFallback_ReturnsFieldError()
    {
        var request = ValidRequest();
        request.AllowLightningFallback = false;
        request.FailureBehavior = "LightningFallback";

        var result = ProtectedActionPolicy.Evaluate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainKey(nameof(request.FailureBehavior));
    }

    private static UpsertProtectedActionRequest ValidRequest()
    {
        return new UpsertProtectedActionRequest
        {
            Environment = "TEST",
            Name = "ai.generate_image",
            DisplayName = "Generate AI Image",
            Description = "Protect an expensive image-generation request.",
            BaseDifficulty = 17,
            SuspiciousDifficulty = 20,
            MaximumDifficulty = 24,
            AnonymousRequestLimit = 5,
            AnonymousLimitWindowSeconds = 3600,
            TokenLifetimeSeconds = 120,
            FailureBehavior = "Deny",
            LightningPriceSats = 25,
            LightningFallbackMode = "RateLimitOnly",
            EstimatedCostPerExecution = 0.02m
        };
    }
}
