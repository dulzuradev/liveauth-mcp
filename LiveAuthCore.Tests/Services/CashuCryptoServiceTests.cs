using System.Text.RegularExpressions;
using FluentAssertions;
using LiveAuthCore.Services;
using Xunit;

namespace LiveAuthCore.Tests.Services;

public class CashuCryptoServiceTests
{
    [Theory]
    [InlineData(0, new long[] { })]
    [InlineData(1, new[] { 1L })]
    [InlineData(13, new[] { 1L, 4L, 8L })]
    [InlineData(255, new[] { 1L, 2L, 4L, 8L, 16L, 32L, 64L, 128L })]
    public void DecomposeAmount_ReturnsPowersOfTwo(long amount, long[] expected)
    {
        CashuCryptoService.DecomposeAmount(amount).Should().Equal(expected);
    }

    [Fact]
    public void GenerateSecret_ReturnsLowercaseThirtyTwoByteHex()
    {
        var secret = CashuCryptoService.GenerateSecret();

        secret.Should().HaveLength(64);
        Regex.IsMatch(secret, "^[0-9a-f]{64}$").Should().BeTrue();
    }

    [Fact]
    public void HashToCurve_IsDeterministicSha256Hash()
    {
        var first = CashuCryptoService.HashToCurve("liveauth-secret");
        var second = CashuCryptoService.HashToCurve("liveauth-secret");
        var different = CashuCryptoService.HashToCurve("other-secret");

        first.Should().HaveCount(32);
        first.Should().Equal(second);
        first.Should().NotEqual(different);
    }

    [Fact]
    public void CreateBlindedMessage_ReturnsHexEncodedMessageSecretAndBlindingFactor()
    {
        var (blindedMessage, secret, blindingFactor) = CashuCryptoService.CreateBlindedMessage();

        blindedMessage.Should().HaveLength(66);
        secret.Should().HaveLength(64);
        blindingFactor.Should().HaveLength(64);
        Regex.IsMatch(blindedMessage, "^[0-9a-f]{66}$").Should().BeTrue();
        Regex.IsMatch(secret, "^[0-9a-f]{64}$").Should().BeTrue();
        Regex.IsMatch(blindingFactor, "^[0-9a-f]{64}$").Should().BeTrue();
    }

    [Fact]
    public void UnblindSignature_WithSameInputs_ReturnsStableHexOutput()
    {
        var (blindedMessage, _, blindingFactor) = CashuCryptoService.CreateBlindedMessage();
        const string mintPublicKey = "0279be667ef9dcbbac55a06295ce870b07029bfcdb2dce28d959f2815b16f81798";

        var first = CashuCryptoService.UnblindSignature(blindedMessage, blindingFactor, mintPublicKey);
        var second = CashuCryptoService.UnblindSignature(blindedMessage, blindingFactor, mintPublicKey);

        first.Should().HaveLength(66);
        first.Should().Be(second);
        Regex.IsMatch(first, "^[0-9a-f]{66}$").Should().BeTrue();
    }
}
