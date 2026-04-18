using System.Security.Cryptography;
using System.Text;
using LiveAuthCore.Data.Entities;
using Microsoft.Extensions.Caching.Memory;

namespace LiveAuthCore.Services;

/// <summary>
/// L402 Payment Gateway Service
/// Handles Lightning invoice creation and L402 token validation.
/// </summary>
public class L402Service
{
    private readonly LightningService _lightning;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _config;
    
    // Default: 1 sat per request, 1 hour TTL
    private const int DefaultSatsPerRequest = 1;
    private const int McpSatsPerRequest = 10;
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(1);

    public L402Service(LightningService lightning, IMemoryCache cache, IConfiguration config)
    {
        _lightning = lightning;
        _cache = cache;
        _config = config;
    }

    /// <summary>
    /// Create an invoice for L402 payment.
    /// </summary>
    public async Task<L402InvoiceResponse> CreateInvoiceAsync(string? destination, int? amountSats = null)
    {
        var sats = amountSats ?? DefaultSatsPerRequest;
        var memo = string.IsNullOrEmpty(destination) 
            ? "LiveAuth L402 access" 
            : $"LiveAuth L402 access for {destination}";
        
        var result = await _lightning.CreateLoginInvoiceAsync(
            email: destination ?? "anonymous",
            amountSats: sats,
            expiryMinutes: 10 // Invoice expires in 10 min, but token valid for 1hr once paid
        );

        // Preimage is used as the L402 token - we derive a token hash for lookup
        // LND doesn't give us the preimage directly on create, but we can use r_hash as the payment hash
        return new L402InvoiceResponse
        {
            PaymentHash = result.InvoiceId, // base64 r_hash
            Bolt11 = result.Bolt11,
            AmountSats = sats,
            ExpiresAtUnix = result.ExpiresAtUnix,
            // Token will be derived from preimage once invoice is paid
            Token = null 
        };
    }

    /// <summary>
    /// Validate an L402 token (preimage) against a paid invoice.
    /// Returns the token hash if valid, null otherwise.
    /// </summary>
    public async Task<string?> ValidateTokenAsync(string preimage)
    {
        if (string.IsNullOrEmpty(preimage))
            return null;

        // Check cache first
        var tokenHash = ComputeTokenHash(preimage);
        if (_cache.TryGetValue($"l402_token:{tokenHash}", out _))
        {
            return tokenHash;
        }

        // Need to find the invoice that was paid with this preimage
        // In production, we'd need to track this. For now, we do a lookup:
        // Try to look up invoice by payment hash derived from preimage
        // This is tricky because LND doesn't expose "get invoice by preimage"
        
        // Simplified approach: store mapping when invoice is created
        // For now, accept that clients present preimage and we check if it's valid
        // The real validation: prove you know the preimage = you paid the invoice
        
        // TODO: In v2, track (preimageHash -> paymentHash) at invoice creation time
        
        return null; // Simplified - needs proper implementation
    }

    /// <summary>
    /// Mark a preimage as validated and issue an L402 token.
    /// Called after successful payment verification.
    /// </summary>
    public async Task<string> IssueTokenAsync(string paymentHashBase64)
    {
        // Check if already issued
        var existingToken = GetTokenByPaymentHash(paymentHashBase64);
        if (existingToken != null)
            return existingToken;

        // Verify payment
        var status = await _lightning.GetInvoiceStatusAsync(paymentHashBase64);
        if (!status.IsPaid)
            return string.Empty;

        // Generate token from payment hash
        // In v2, we'd store preimage and use that
        var token = GenerateTokenFromPaymentHash(paymentHashBase64);
        
        // Cache token with TTL
        var ttl = GetTokenTtl();
        _cache.Set($"l402_token:{token}", true, ttl);
        _cache.Set($"l402_payment:{paymentHashBase64}", token, ttl);
        
        return token;
    }

    private string GenerateTokenFromPaymentHash(string paymentHash)
    {
        // Use payment hash as token base (simpler than preimage for v1)
        // Token = base64(payment_hash) for easier handling
        var normalized = paymentHash.Replace("-", "+").Replace("_", "/");
        while (normalized.Length % 4 != 0) normalized += "=";
        return normalized;
    }

    private string? GetTokenByPaymentHash(string paymentHash)
    {
        if (_cache.TryGetValue($"l402_payment:{paymentHash}", out string? token))
            return token;
        return null;
    }

    /// <summary>
    /// Check if an L402 token is currently valid.
    /// </summary>
    public bool IsTokenValid(string token)
    {
        if (string.IsNullOrEmpty(token))
            return false;

        return _cache.TryGetValue($"l402_token:{token}", out _);
    }

    /// <summary>
    /// Store preimage mapping when creating invoice (for later validation).
    /// </summary>
    public void StorePreimageMapping(string paymentHashBase64, string preimage)
    {
        // Cache for 10 min (invoice expiry time)
        _cache.Set($"l402_preimage:{paymentHashBase64}", preimage, TimeSpan.FromMinutes(10));
    }

    /// <summary>
    /// Compute SHA256 hash of token for lookup.
    /// </summary>
    public static string ComputeTokenHash(string preimage)
    {
        var bytes = Encoding.UTF8.GetBytes(preimage);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Issue a macaroon credential for a bundle.
    /// Returns (macaroon_base64, signature_base64).
    /// </summary>
    public (string Macaroon, string Signature) IssueMacaroonForBundle(L402Bundle bundle)
    {
        var jti = $"tok_{Guid.NewGuid().ToString("N")[..12]}";
        var scopes = new[] { "mcp.verify", "auth.start" };
        var scopesJson = System.Text.Json.JsonSerializer.Serialize(scopes);

        var claims = new Dictionary<string, object>
        {
            ["kid"] = bundle.ProjectId.ToString(),
            ["aid"] = bundle.AgentId ?? "default",
            ["scopes"] = scopes,
            ["bid"] = bundle.BundleId,
            ["rate"] = bundle.RemainingCalls,
            ["exp"] = bundle.ExpiresAtUnix,
            ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["jti"] = jti
        };

        // Encode claims as CBOR-like JSON (simplified for v1)
        var claimsJson = System.Text.Json.JsonSerializer.Serialize(claims);
        var claimsB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(claimsJson));

        // Sign with HMAC-SHA256
        var signingKey = GetMacaroonSigningKey();
        using var hmac = new System.Security.Cryptography.HMACSHA256(signingKey);
        var sigBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(claimsB64));
        var sigB64 = Convert.ToBase64String(sigBytes);

        var macaroon = $"{claimsB64}.{sigB64}";
        return (macaroon, sigB64);
    }

    private static byte[] GetMacaroonSigningKey()
    {
        // In production, this should come from config (per-project or global)
        var secret = "liveauth-macaroon-secret-v1";
        using var sha = System.Security.Cryptography.SHA256.Create();
        return sha.ComputeHash(Encoding.UTF8.GetBytes(secret));
    }

    private static string GenerateMockPreimage(string paymentHash)
    {
        // Generate deterministic mock preimage for testing
        var input = $"mock-preimage-{paymentHash}";
        var bytes = Encoding.UTF8.GetBytes(input);
        return Convert.ToBase64String(SHA256.HashData(bytes))[..64];
    }

    private TimeSpan GetTokenTtl()
    {
        var ttlMinutes = _config.GetValue<int?>("L402:TokenTtlMinutes") ?? 60;
        return TimeSpan.FromMinutes(ttlMinutes);
    }

    /// <summary>
    /// Get configured price for an endpoint.
    /// </summary>
    public int GetPriceForEndpoint(string path)
    {
        // MCP endpoints cost more
        if (path.StartsWith("/api/mcp", StringComparison.OrdinalIgnoreCase))
            return McpSatsPerRequest;
        
        return _config.GetValue<int?>("L402:DefaultSats") ?? DefaultSatsPerRequest;
    }
}

public class L402InvoiceResponse
{
    public string PaymentHash { get; set; } = string.Empty;
    public string Bolt11 { get; set; } = string.Empty;
    public int AmountSats { get; set; }
    public long ExpiresAtUnix { get; set; }
    public string? Token { get; set; } // Filled after payment verification
}
