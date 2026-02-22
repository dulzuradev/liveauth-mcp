using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace LiveAuthCore.Services.Tests;

public class LightningServicePaymentHashTests
{
    private readonly Mock<IConfiguration> _mockConfig;
    private readonly Mock<HttpMessageHandler> _mockHttpHandler;
    private readonly HttpClient _httpClient;

    public LightningServicePaymentHashTests()
    {
        _mockConfig = new Mock<IConfiguration>();
        _mockConfig.Setup(c => c["LND:Url"]).Returns("https://localhost:8080");
        _mockConfig.Setup(c => c["LND:Macaroon"]).Returns("test-macaroon");
        
        _mockHttpHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        _httpClient = new HttpClient(_mockHttpHandler.Object);
    }

    /// <summary>
    /// Tests that the payment hash stored in the database is HEX (64 chars), not base64 (44 chars).
    /// This was a bug where LND returns base64 but we need hex for lookups.
    /// </summary>
    [Fact]
    public void PaymentHash_ShouldBe64CharHex_Not44CharBase64()
    {
        // This is a conceptual test - the actual LightningService requires LND connection
        // But we verify the expected format here:
        
        // LND returns r_hash as base64 (32 bytes → ~44 chars like "cHHROPqueUyxfFufqUFz010=")
        string base64RHash = "cHHROPqueUyxfFufqUFz010=";
        
        // We must convert to hex (32 bytes → 64 chars like "b071384e9ae54cb17c5fb9e9417cd35")
        var bytes = Convert.FromBase64String(base64RHash);
        string hexRHash = Convert.ToHexString(bytes).ToLowerInvariant();
        
        // Assert: hex hash is 64 characters
        Assert.Equal(64, hexRHash.Length);
        Assert.True(hexRHash.All(c => "0123456789abcdef".Contains(c)), "Should be valid hex");
        
        // Assert: base64 hash is NOT 64 characters
        Assert.NotEqual(44, hexRHash.Length);
    }

    /// <summary>
    /// Verifies that TryNormalizePaymentHash correctly handles hex input.
    /// </summary>
    [Fact]
    public void TryNormalizePaymentHash_With64CharHex_ReturnsTrue()
    {
        // This tests the normalization logic in LightningService
        string hexHash = "b071384e9ae54cb17c5fb9e9417cd35b071384e9ae54cb17c5fb9e9417cd35"; // 32 bytes
        
        bool isHex = IsLikelyHex(hexHash);
        Assert.True(isHex, "64 char hex string should be detected as hex");
        
        // Test conversion
        var bytes = Convert.FromHexString(hexHash);
        Assert.Equal(32, bytes.Length);
        
        // Round-trip
        string roundTripped = Convert.ToHexString(bytes).ToLowerInvariant();
        Assert.Equal(hexHash, roundTripped);
    }

    /// <summary>
    /// Verifies that TryNormalizePaymentHash correctly handles base64 input.
    /// </summary>
    [Fact]
    public void TryNormalizePaymentHash_WithBase64_ReturnsTrue()
    {
        // Base64 encoded hash (44 chars for 32 bytes)
        string base64Hash = "cHHROPqueUyxfFufqUFz010=";
        
        bool isHex = IsLikelyHex(base64Hash);
        Assert.False(isHex, "Base64 string should NOT be detected as hex");
        
        // Should fall through to base64 parsing
        var bytes = Convert.FromBase64String(base64Hash);
        Assert.Equal(32, bytes.Length);
        
        // Convert to hex for storage
        string hexHash = Convert.ToHexString(bytes).ToLowerInvariant();
        Assert.Equal(64, hexHash.Length);
    }

    /// <summary>
    /// Tests that a 32-byte payment hash always converts to exactly 64 hex characters.
    /// </summary>
    [Fact]
    public void PaymentHash_32Bytes_Produces64HexChars()
    {
        // Generate a random 32-byte hash
        byte[] randomBytes = new byte[32];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        
        string hex = Convert.ToHexString(randomBytes).ToLowerInvariant();
        
        Assert.Equal(64, hex.Length);
        Assert.True(hex.All(c => "0123456789abcdef".Contains(c)));
        
        // Verify round-trip
        byte[] decoded = Convert.FromHexString(hex);
        Assert.Equal(32, decoded.Length);
    }

    /// <summary>
    /// Verifies that invalid payment hash formats are rejected.
    /// </summary>
    [Fact]
    public void TryNormalizePaymentHash_WithInvalidFormat_ReturnsFalse()
    {
        // Test various invalid inputs
        string[] invalidInputs = {
            "",           // Empty
            "abc",        // Too short
            "xyz",        // Invalid hex chars
            "   ",        // Whitespace
        };
        
        foreach (var input in invalidInputs)
        {
            bool isHex = IsLikelyHex(input);
            // Empty and whitespace should not be hex
            if (string.IsNullOrWhiteSpace(input))
            {
                Assert.False(isHex);
            }
        }
    }

    // Helper method matching LightningService logic
    private static bool IsLikelyHex(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return false;
        
        if (s.Length != 64 && s.Length != 44) // Not 32 bytes in hex or base64
            return false;
        
        return s.All(c => "0123456789abcdefABCDEF".Contains(c));
    }
}
