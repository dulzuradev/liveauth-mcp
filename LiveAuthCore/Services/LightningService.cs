using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.IdentityModel.Tokens;

namespace LiveAuthCore.Services;

public class LightningService
{
    private readonly string _baseUrl;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly bool _useMock;
    private readonly bool _mockLoginIdentity;

    // Cache macaroon so we don't read disk every call
    private string? _macaroonHexCache;

    public LightningService(IConfiguration configuration)
    {
        _configuration = configuration;

        // Read LND REST configuration
        _baseUrl = (_configuration["Lnd:BaseUrl"] ?? "https://127.0.0.1:8283").TrimEnd('/');
        _useMock = bool.TryParse(_configuration["Lnd:UseMock"], out var mock) && mock;
        _mockLoginIdentity = bool.TryParse(
                                 _configuration["DevLogin:MockLightningIdentity"], out var mockId)
                             && mockId;

        // Configure HttpClient (allow self-signed for dev/test)
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };

        _httpClient = new HttpClient(handler);
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    // ------------------------------------------------------------------------
    // PUBLIC API
    // ------------------------------------------------------------------------

    /// <summary>
    /// Generic invoice creation (existing behavior).
    /// </summary>
    public async Task<InvoiceResponse> CreateInvoice(string userId, long amountSats, string memo)
    {
        await EnsureMacaroonHeaderAsync();

        var url = $"{_baseUrl}/v1/invoices";

        var requestBody = new
        {
            memo,
            value_msat = amountSats * 1000L,
            expiry = "3600",
            @private = true
        };

        var jsonBody = JsonSerializer.Serialize(requestBody,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(url, content);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new ApplicationException(
                $"Failed to create invoice. Status: {response.StatusCode}. Response: {errorBody}");
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        var invoice = JsonSerializer.Deserialize<InvoiceResponse>(responseJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return invoice ?? throw new ApplicationException("LND returned null invoice");
    }

    /// <summary>
    /// NEW: Create a login-specific invoice for dev auth flow.
    /// Wraps LND /v1/invoices and returns an object with Id + BOLT11 string.
    /// </summary>
    public async Task<LoginInvoiceResult> CreateLoginInvoiceAsync(
        string email,
        long amountSats,
        int expiryMinutes)
    {
        if (_useMock)
        {
            var now = DateTimeOffset.UtcNow;
            return new LoginInvoiceResult
            {
                InvoiceId = Guid.NewGuid().ToString("N"),
                Bolt11 = "lnmock1devlogininvoice",
                AmountSats = amountSats,
                ExpiresAtUnix = now.AddMinutes(expiryMinutes).ToUnixTimeSeconds()
            };
        }

        await EnsureMacaroonHeaderAsync();

        var url = $"{_baseUrl}/v1/invoices";

        var requestBody = new
        {
            memo = $"LiveAuth dev login for {email}",
            value_msat = amountSats * 1000L,
            expiry = (expiryMinutes * 60).ToString(),
            // keep it private (don't broadcast channels)
            @private = true
        };

        var jsonBody = JsonSerializer.Serialize(requestBody,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, content);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new ApplicationException(
                $"Failed to create login invoice. Status: {response.StatusCode}. Response: {errorBody}");
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        var invoice = JsonSerializer.Deserialize<InvoiceResponse>(responseJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (invoice == null)
            throw new ApplicationException("LND returned null invoice for login");

        var expiresAtUnix = DateTimeOffset.UtcNow.AddMinutes(expiryMinutes).ToUnixTimeSeconds();

        return new LoginInvoiceResult
        {
            // r_hash is base64; we treat that as "InvoiceId" for lookup
            InvoiceId = invoice.RHash,
            Bolt11 = invoice.PaymentRequest,
            AmountSats = amountSats,
            ExpiresAtUnix = expiresAtUnix
        };
    }

    /// <summary>
    /// NEW: Get detailed status for a login invoice.
    /// Uses the same underlying invoice lookup as CheckPaymentStatus but
    /// returns a richer object (IsPaid + placeholder Lightning identity).
    /// </summary>
    public async Task<InvoiceStatusResult> GetInvoiceStatusAsync(string paymentHashB64)
    {
        //
        // MOCK MODE
        //
        if (_useMock)
        {
            bool mockIsPaid = true; // mock always succeeds

            string? mockPayerKey = null;
            if (_mockLoginIdentity && mockIsPaid)
            {
                // Deterministic fake payer identity hashed from input
                using var sha = SHA256.Create();
                var bytes = Encoding.UTF8.GetBytes($"mock-login-{paymentHashB64}");
                var hash = sha.ComputeHash(bytes);
                mockPayerKey = "lnmock_" + Convert.ToHexString(hash).ToLowerInvariant();
            }

            return new InvoiceStatusResult
            {
                IsPaid = mockIsPaid,
                PayerLightningAuthKey = mockPayerKey
            };
        }


        //
        // REAL LND MODE
        //
        if (string.IsNullOrWhiteSpace(paymentHashB64))
        {
            return new InvoiceStatusResult
            {
                IsPaid = false,
                PayerLightningAuthKey = null
            };
        }

        await EnsureMacaroonHeaderAsync();
        paymentHashB64 = paymentHashB64.Trim();

        // Normalize 32-byte payment hash
        if (!TryNormalizePaymentHash(paymentHashB64, out var rHashBytes, out var error))
        {
            return new InvoiceStatusResult
            {
                IsPaid = false,
                PayerLightningAuthKey = null
            };
        }

        var hex = Convert.ToHexString(rHashBytes!).ToLowerInvariant();
        var url = $"{_baseUrl}/v1/invoice/{hex}";
        var response = await _httpClient.GetAsync(url);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new InvoiceStatusResult
            {
                IsPaid = false,
                PayerLightningAuthKey = null
            };
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();

            if (body.Contains("must be exactly 32 bytes", StringComparison.OrdinalIgnoreCase))
            {
                return new InvoiceStatusResult
                {
                    IsPaid = false,
                    PayerLightningAuthKey = null
                };
            }

            throw new ApplicationException(
                $"LookupInvoice failed. Status: {response.StatusCode}. Body: {body}");
        }

        var json = await response.Content.ReadAsStringAsync();
        var invoice = JsonSerializer.Deserialize<InvoiceResponse>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );

        bool isSettled = invoice?.Settled == true;

        string? lightningAuthKey = null;

        if (_mockLoginIdentity && isSettled)
        {
            using var sha = SHA256.Create();
            var source = invoice?.PaymentRequest ?? hex;
            var bytes = Encoding.UTF8.GetBytes(source);
            var hash = sha.ComputeHash(bytes);
            lightningAuthKey = "lnmock_" + Convert.ToHexString(hash).ToLowerInvariant();
        }

        return new InvoiceStatusResult
        {
            IsPaid = isSettled,
            PayerLightningAuthKey = lightningAuthKey
        };
    }


    /// <summary>
    /// Normalize an input payment hash string to 32 bytes.
    /// Accepts base64, base64url, or hex. Returns false if cannot decode or wrong length.
    /// </summary>
    private bool TryNormalizePaymentHash(string input, out byte[]? bytes, out string? error)
    {
        bytes = null;
        error = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "Empty payment hash";
            return false;
        }

        var s = input.Trim();

        // 🔧 If this came from a URL path segment, it may contain %xx escapes
        // or '+' turned into spaces. Normalize that first.
        try
        {
            s = Uri.UnescapeDataString(s);
        }
        catch
        {
            // if this fails, we'll still fall back to base64/hex attempts
        }

        // '+' is valid in base64, but if it went through a query/path without proper encoding,
        // it might have been turned into spaces. Convert them back.
        s = s.Replace(' ', '+');

        // ------------------------------
        // 1) Try HEX first (64 hex chars => 32 bytes)
        // ------------------------------
        if (IsLikelyHex(s))
        {
            try
            {
                // pad to even length if needed
                var hex = s.Length % 2 == 0 ? s : "0" + s;
                bytes = Convert.FromHexString(hex);
            }
            catch
            {
                bytes = null;
            }

            if (bytes?.Length == 32)
                return true;

            // hex path failed length requirement; fall through to base64
            error = "Hex decoded length not 32 bytes";
            bytes = null;
        }

        // ------------------------------
        // 2) Treat as base64 / base64url
        // ------------------------------
        var b64 = s.Replace('-', '+').Replace('_', '/');

        // add missing padding if any
        var pad = b64.Length % 4;
        if (pad > 0)
            b64 = b64.PadRight(b64.Length + (4 - pad), '=');

        try
        {
            var tmp = Convert.FromBase64String(b64);
            if (tmp.Length == 32)
            {
                bytes = tmp;
                return true;
            }

            // base64 decoded, but not 32 bytes
            error = $"Base64 decoded length is {tmp.Length}, expected 32";
            return false;
        }
        catch (FormatException)
        {
            error = "Not valid base64/base64url or hex";
            return false;
        }
    }


    private static bool IsLikelyHex(string s)
    {
        if (s.Length < 2) return false;
        foreach (var c in s)
        {
            bool isHex = (c >= '0' && c <= '9') ||
                         (c >= 'a' && c <= 'f') ||
                         (c >= 'A' && c <= 'F');
            if (!isHex) return false;
        }

        return true;
    }

    /// <summary>
    /// Legacy helper - now delegates to GetInvoiceStatusAsync.
    /// </summary>
    public async Task<bool> CheckPaymentStatus(string paymentHashB64)
    {
        var status = await GetInvoiceStatusAsync(paymentHashB64);
        return status.IsPaid;
    }

    // -----------------------------------------------------------------------
    // JWT (canonical)
    // -----------------------------------------------------------------------

    // Back-compat: existing code calls this
    public string GenerateJwtToken(string userId)
    {
        // Preserve your old behavior: "admin" => Admin else User
        var role = string.Equals(userId, "admin", StringComparison.OrdinalIgnoreCase)
            ? "Admin"
            : "User";

        return GenerateJwtToken(userId, role);
    }

    // Back-compat: existing code calls this
    public string GenerateJwtToken(string userId, string role)
    {
        return GenerateJwtTokenCore(
            subjectUserId: userId,
            role: role,
            extraClaims: null,
            expiresUtc: DateTime.UtcNow.AddMinutes(30)
        );
    }

    /// <summary>
    /// Preferred overload for new code (optional extra claims + expiry).
    /// </summary>
    public string GenerateJwtToken(
        string userId,
        string role,
        IEnumerable<Claim>? extraClaims,
        DateTime? expiresUtc = null)
    {
        return GenerateJwtTokenCore(
            subjectUserId: userId,
            role: role,
            extraClaims: extraClaims,
            expiresUtc: expiresUtc ?? DateTime.UtcNow.AddMinutes(30)
        );
    }

    // The ONE canonical implementation
    private string GenerateJwtTokenCore(
        string subjectUserId,
        string role,
        IEnumerable<Claim>? extraClaims,
        DateTime expiresUtc,
        string? audienceOverride = null)
    {
        if (string.IsNullOrWhiteSpace(subjectUserId))
            throw new ArgumentException("userId is required.", nameof(subjectUserId));

        if (string.IsNullOrWhiteSpace(role))
            role = "User";

        // Prefer SigningKey; fall back to Key (you've used both in the codebase)
        var signingKey =
            _configuration["Jwt:SigningKey"] ??
            _configuration["Jwt:Key"];

        if (string.IsNullOrWhiteSpace(signingKey))
            throw new InvalidOperationException(
                "JWT signing key missing. Configure Jwt:SigningKey (preferred) or Jwt:Key.");

        var issuer = _configuration["Jwt:Issuer"];
        var audience =
            audienceOverride ??
            _configuration["Jwt:Audience"];

        if (string.IsNullOrWhiteSpace(issuer))
            throw new InvalidOperationException("Jwt:Issuer is not configured.");
        if (string.IsNullOrWhiteSpace(audience))
            throw new InvalidOperationException("Jwt:Audience is not configured.");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // Base claims (consistent everywhere)
        var claims = new List<Claim>
        {
            new Claim("userId", subjectUserId),
            new Claim(ClaimTypes.Role, role)
        };

        if (extraClaims != null)
        {
            // Avoid duplicate role/userId claims if caller passes them
            foreach (var c in extraClaims)
            {
                if (c.Type == "userId" || c.Type == ClaimTypes.Role) continue;
                claims.Add(c);
            }
        }

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expiresUtc,
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task<(string PaymentHash, string Preimage)> PayInvoice(string invoice)
    {
        await EnsureMacaroonHeaderAsync();

        try
        {
            var url = $"{_baseUrl}/v2/router/send";
            var requestBody = new
            {
                payment_request = invoice,
                timeout_seconds = 60,
                fee_limit_sat = 10
            };

            var jsonBody = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content);
            response.EnsureSuccessStatusCode();

            var responseData = await response.Content.ReadAsStringAsync();
            var payment = JsonSerializer.Deserialize<PaymentResponse>(responseData,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (payment == null)
                throw new ApplicationException("LND returned null payment response");

            return (payment.PaymentHash, payment.PaymentPreimage);
        }
        catch (Exception ex)
        {
            throw new ApplicationException("Failed to pay invoice.", ex);
        }
    }

    // ------------------------------------------------------------------------
    // INTERNAL HELPERS
    // ------------------------------------------------------------------------

    private async Task EnsureMacaroonHeaderAsync()
    {
        var configured = _configuration["Lnd:Macaroon"];

        if (!string.IsNullOrWhiteSpace(configured))
        {
            // normalize configured macaroon to HEX if needed
            var hex = NormalizeMacaroonToHex(configured.Trim());
            SetMacaroonHeader(hex);
            return;
        }

        // Otherwise lazy-load Polar macaroon once
        if (_macaroonHexCache == null)
            _macaroonHexCache = await GetPolarMacaroonHexAsync();

        SetMacaroonHeader(_macaroonHexCache);
    }

    private void SetMacaroonHeader(string macaroonHex)
    {
        const string headerName = "Grpc-Metadata-macaroon";

        if (_httpClient.DefaultRequestHeaders.Contains(headerName))
            _httpClient.DefaultRequestHeaders.Remove(headerName);

        _httpClient.DefaultRequestHeaders.Add(headerName, macaroonHex);
    }

    private static string NormalizeMacaroonToHex(string macaroon)
    {
        // If it's already hex (even length, only hex chars), use as-is
        if (IsHex(macaroon))
            return macaroon.ToLowerInvariant();

        // Otherwise treat as base64 and convert to hex
        try
        {
            var bytes = Convert.FromBase64String(macaroon);
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
        catch (FormatException fe)
        {
            throw new ApplicationException(
                "Configured Lnd:Macaroon is neither valid hex nor valid base64. " +
                "Provide hex macaroon or base64 macaroon.",
                fe);
        }
    }

    private static bool IsHex(string s)
    {
        if (s.Length % 2 != 0) return false;
        foreach (var c in s)
        {
            var isHexChar =
                (c >= '0' && c <= '9') ||
                (c >= 'a' && c <= 'f') ||
                (c >= 'A' && c <= 'F');
            if (!isHexChar) return false;
        }

        return true;
    }

    private async Task<string> GetPolarMacaroonHexAsync()
    {
        var macaroonPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".polar",
            "networks",
            "1",
            "volumes",
            "lnd",
            "alice",
            "data",
            "chain",
            "bitcoin",
            "regtest",
            "admin.macaroon");

        if (!File.Exists(macaroonPath))
        {
            throw new FileNotFoundException(
                $"Macaroon not found at {macaroonPath}\n" +
                $"Check Polar network '1' is running and Alice is unlocked.");
        }

        var bytes = await File.ReadAllBytesAsync(macaroonPath);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public string GenerateAdminJwtToken(
        string adminId,
        IEnumerable<Claim>? extraClaims = null)
    {
        return GenerateJwtTokenCore(
            subjectUserId: adminId,
            role: "Admin",
            extraClaims: extraClaims,
            expiresUtc: DateTime.UtcNow.AddHours(8),
            audienceOverride: "LiveAuthAdmin"
        );
    }

    public string GenerateDeveloperJwtToken(
        string developerId,
        IEnumerable<Claim>? extraClaims = null)
    {
        return GenerateJwtTokenCore(
            subjectUserId: developerId,
            role: "Developer",
            extraClaims: extraClaims,
            expiresUtc: DateTime.UtcNow.AddHours(2),
            audienceOverride: "LiveAuthDevelopers"
        );
    }

    // ------------------------------------------------------------------------
    // DTOs
    // ------------------------------------------------------------------------

    public class InvoiceResponse
    {
        [JsonPropertyName("r_hash")] public string RHash { get; set; } = string.Empty;
        [JsonPropertyName("payment_request")] public string PaymentRequest { get; set; } = string.Empty;
        [JsonPropertyName("add_index")] public string AddIndex { get; set; } = string.Empty;
        [JsonPropertyName("payment_addr")] public string PaymentAddr { get; set; } = string.Empty;
        [JsonPropertyName("settled")] public bool Settled { get; set; }
    }

    private class PaymentResponse
    {
        [JsonPropertyName("payment_hash")] public string PaymentHash { get; set; } = string.Empty;
        [JsonPropertyName("payment_preimage")] public string PaymentPreimage { get; set; } = string.Empty;
    }
}

// Result type for login invoices (used by DevAuthController)
public sealed class LoginInvoiceResult
{
    public string InvoiceId { get; set; } = string.Empty; // we use r_hash (base64) here
    public string Bolt11 { get; set; } = string.Empty;
    public long AmountSats { get; set; }
    public long ExpiresAtUnix { get; set; }
}

// Result type for invoice status polling (used by DevAuthController)
public sealed class InvoiceStatusResult
{
    public bool IsPaid { get; set; }

    /// <summary>
    /// Lightning identity of payer (LNURL-auth pubkey, etc).
    /// Currently null until you wire that in.
    /// </summary>
    public string? PayerLightningAuthKey { get; set; }
}