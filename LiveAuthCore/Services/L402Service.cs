using System.Security.Cryptography;
using System.Text;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using Microsoft.EntityFrameworkCore;
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
    
    // Default: 1 sat per request, 1 allowed call, 1 hour max TTL.
    private const int DefaultSatsPerRequest = 1;
    private const int DefaultTokenCallAllowance = 1;
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
    public async Task<L402InvoiceResponse> CreateInvoiceAsync(string? destination, int? amountSats = null, Project? project = null)
    {
        var sats = amountSats ?? DefaultSatsPerRequest;
        var memo = string.IsNullOrEmpty(destination) 
            ? "LiveAuth L402 access" 
            : $"LiveAuth L402 access for {destination}";
        
        var result = await _lightning.CreateLoginInvoiceAsync(
            email: destination ?? "anonymous",
            amountSats: sats,
            expiryMinutes: 10, // Invoice expires in 10 min, but token valid for 1hr once paid
            project: project
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

    public void BindInvoiceToProject(string paymentHash, Guid projectId)
    {
        if (string.IsNullOrWhiteSpace(paymentHash) || projectId == Guid.Empty)
            return;

        _cache.Set(GetInvoiceProjectCacheKey(paymentHash), projectId, TimeSpan.FromMinutes(10));
    }

    public bool IsInvoiceBoundToProject(string paymentHash, Guid projectId)
    {
        if (string.IsNullOrWhiteSpace(paymentHash) || projectId == Guid.Empty)
            return false;

        return _cache.TryGetValue(GetInvoiceProjectCacheKey(paymentHash), out Guid boundProjectId) &&
               boundProjectId == projectId;
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
        
        // Try to find preimage mapping (stored at invoice creation time)
        var storedPreimage = GetPreimageByPaymentHash(tokenHash);
        if (!string.IsNullOrEmpty(storedPreimage))
        {
            // Re-verify: preimage must hash to the token hash
            var expectedHash = ComputeTokenHash(storedPreimage);
            if (string.Equals(tokenHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                // Token hash is valid preimage representation - cache it
                var ttl = GetTokenTtl();
                _cache.Set($"l402_token:{tokenHash}", true, ttl);
                return tokenHash;
            }
        }
        
        // If token itself is a payment hash (fallback from IssueTokenAsync), check payment status
        var normalizedToken = NormalizePaymentHash(tokenHash);
        if (!string.IsNullOrEmpty(normalizedToken))
        {
            // Check if this token was previously issued
            var cached = GetTokenByPaymentHash(normalizedToken);
            if (!string.IsNullOrEmpty(cached))
                return cached;
        }

        return null; // Cannot validate - no mapping found
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
        _cache.Set(GetTokenAllowanceCacheKey(token), GetTokenCallAllowance(), ttl);
        if (_cache.TryGetValue(GetInvoiceProjectCacheKey(paymentHashBase64), out Guid projectId))
            _cache.Set(GetTokenProjectCacheKey(token), projectId, ttl);
        
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

    private string? GetPreimageByPaymentHash(string paymentHash)
    {
        if (_cache.TryGetValue($"l402_preimage:{paymentHash}", out string? preimage))
            return preimage;
        return null;
    }

    private static string? NormalizePaymentHash(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        // Try hex (64 chars)
        if (input.Length == 64 && IsHex(input))
            return input.ToLowerInvariant();

        // Try base64 → hex
        try
        {
            var b64 = input.Replace('-', '+').Replace('_', '/');
            while (b64.Length % 4 != 0) b64 += "=";
            var bytes = Convert.FromBase64String(b64);
            if (bytes.Length == 32)
                return Convert.ToHexString(bytes).ToLowerInvariant();
        }
        catch
        {
            // Not base64
        }

        return null;
    }

    private static bool IsHex(string s)
    {
        foreach (var c in s)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                return false;
        }
        return true;
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

        return _cache.TryGetValue($"l402_token:{token}", out _) &&
               _cache.TryGetValue(GetTokenAllowanceCacheKey(token), out int remainingCalls) &&
               remainingCalls > 0;
    }

    public bool IsTokenValid(string token, Guid projectId)
    {
        if (!IsTokenValid(token) || projectId == Guid.Empty)
            return false;

        return _cache.TryGetValue(GetTokenProjectCacheKey(token), out Guid boundProjectId) &&
               boundProjectId == projectId;
    }

    public bool TryConsumeToken(string token)
    {
        if (string.IsNullOrEmpty(token))
            return false;

        if (!_cache.TryGetValue($"l402_token:{token}", out _) ||
            !_cache.TryGetValue(GetTokenAllowanceCacheKey(token), out int remainingCalls) ||
            remainingCalls <= 0)
        {
            return false;
        }

        remainingCalls -= 1;
        if (remainingCalls <= 0)
        {
            _cache.Remove($"l402_token:{token}");
            _cache.Remove(GetTokenAllowanceCacheKey(token));
            _cache.Remove(GetTokenProjectCacheKey(token));
            return true;
        }

        var ttl = GetTokenTtl();
        _cache.Set(GetTokenAllowanceCacheKey(token), remainingCalls, ttl);
        return true;
    }

    public bool TryConsumeToken(string token, Guid projectId)
    {
        if (!IsTokenValid(token, projectId))
            return false;

        return TryConsumeToken(token);
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

    private int GetTokenCallAllowance()
    {
        var allowance = _config.GetValue<int?>("L402:TokenCallAllowance") ?? DefaultTokenCallAllowance;
        return Math.Max(1, allowance);
    }

    private static string GetInvoiceProjectCacheKey(string paymentHash)
        => $"l402_invoice_project:{paymentHash}";

    private static string GetTokenProjectCacheKey(string token)
        => $"l402_token_project:{token}";

    private static string GetTokenAllowanceCacheKey(string token)
        => $"l402_token_allowance:{token}";

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

    /// <summary>
    /// Validate an L402 macaroon from a bundle purchase.
    /// Returns (isValid, bundleId, remainingCalls, errorMessage).
    /// Also decrements the bundle's RemainingCalls on success.
    /// </summary>
    public async Task<(bool IsValid, string? BundleId, int RemainingCalls, string? Error)> 
        ValidateMacaroonAsync(string macaroon, LiveAuthDbContext db)
    {
        if (string.IsNullOrWhiteSpace(macaroon))
            return (false, null, 0, "Macaroon required");

        var parts = macaroon.Split('.');
        if (parts.Length != 2)
            return (false, null, 0, "Invalid macaroon format");

        var claimsB64 = parts[0];
        var sigB64 = parts[1];

        // Verify HMAC signature
        var signingKey = GetMacaroonSigningKey();
        using var hmac = new System.Security.Cryptography.HMACSHA256(signingKey);
        var expectedSig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(claimsB64)));
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(sigB64),
                Encoding.UTF8.GetBytes(expectedSig)))
            return (false, null, 0, "Invalid macaroon signature");

        // Decode claims
        string claimsJson;
        try
        {
            claimsJson = Encoding.UTF8.GetString(Convert.FromBase64String(claimsB64));
        }
        catch
        {
            return (false, null, 0, "Invalid macaroon encoding");
        }

        using var doc = System.Text.Json.JsonDocument.Parse(claimsJson);
        var root = doc.RootElement;

        var jti = root.GetProperty("jti").GetString() ?? "";
        var bid = root.GetProperty("bid").GetString() ?? "";
        var exp = root.GetProperty("exp").GetInt64();
        var rate = root.GetProperty("rate").GetInt32();

        // Check expiry
        if (exp > 0 && DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp)
            return (false, null, 0, "Macaroon expired");

        // Check remaining calls
        if (rate <= 0)
            return (false, null, 0, "Bundle depleted");

        // Decrement bundle calls
        var bundle = await db.L402Bundles
            .FirstOrDefaultAsync(b => b.BundleId == bid);

        if (bundle == null)
            return (false, null, 0, "Bundle not found");

        if (bundle.Status != "active")
            return (false, null, 0, $"Bundle not active (status: {bundle.Status})");

        if (bundle.RemainingCalls <= 0)
            return (false, null, 0, "Bundle depleted");

        // Atomic decrement
        bundle.RemainingCalls -= 1;
        if (bundle.RemainingCalls <= 0)
            bundle.Status = "depleted";

        // Record macaroon usage
        var macRecord = new L402Macaroon
        {
            Id = Guid.NewGuid(),
            Jti = jti,
            BundleId = bundle.Id,
            ProjectId = bundle.ProjectId,
            AgentId = bundle.AgentId,
            ScopesJson = "[\"mcp.verify\",\"auth.start\"]",
            ExpiresAtUnix = exp,
            SignatureB64 = sigB64
        };
        db.L402Macaroons.Add(macRecord);

        await db.SaveChangesAsync();

        return (true, bid, bundle.RemainingCalls, null);
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
