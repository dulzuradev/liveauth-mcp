using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LiveAuthCore.Data.Entities;
using Microsoft.IdentityModel.Tokens;

namespace LiveAuthCore.Services;

/// <summary>
/// Unified result for invoice creation - always returns hex payment hash.
/// Use this instead of accessing invoice.RHash directly.
/// </summary>
public class InvoiceResult
{
    public string PaymentHash { get; set; } = string.Empty;  // Always 64-char hex
    public string Bolt11 { get; set; } = string.Empty;
    public long AmountSats { get; set; }
    public long ExpiresAtUnix { get; set; }
}

public class LndInfo
{
    public string Version { get; set; } = string.Empty;
    public long BlockHeight { get; set; }
    public int NumActiveChannels { get; set; }
    public int NumPeers { get; set; }
}

public class LndGetInfoResponse
{
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("block_height")] public long BlockHeight { get; set; }
    [JsonPropertyName("num_active_channels")] public int NumActiveChannels { get; set; }
    [JsonPropertyName("num_peers")] public int NumPeers { get; set; }
}

public class LightningService
{
    private readonly string _baseUrl;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly bool _useMock;
    private readonly bool _mockLoginIdentity;

    // Cache macaroon so we don't read disk every call
    private string? _macaroonHexCache;

    // Token expiry settings (from config, with defaults)
    private readonly int _adminTokenExpiryHours;
    private readonly int _developerTokenExpiryHours;
    private readonly int _defaultTokenExpiryMinutes;

    /// <summary>
    /// Gets effective LND config for a project. If UseCustomNode is true on the project,
    /// returns the project's custom config; otherwise returns the default config.
    /// </summary>
    private (string baseUrl, string? macaroonHex) GetEffectiveLndConfig(Project? project)
    {
        if (project?.UseCustomNode == true && !string.IsNullOrWhiteSpace(project.LndBaseUrl))
        {
            // Use project's custom LND node
            var macaroonHex = !string.IsNullOrWhiteSpace(project.LndMacaroon)
                ? NormalizeMacaroonToHex(project.LndMacaroon.Trim())
                : null;
            return (project.LndBaseUrl.TrimEnd('/'), macaroonHex);
        }

        // Use default config
        return (_baseUrl, null); // null triggers default macaroon loading
    }

    public LightningService(IConfiguration configuration)
    {
        _configuration = configuration;

        // Read LND REST configuration
        _baseUrl = (_configuration["Lnd:BaseUrl"] ?? "https://127.0.0.1:8283").TrimEnd('/');
        _useMock = bool.TryParse(_configuration["Lnd:UseMock"], out var mock) && mock;
        _mockLoginIdentity = bool.TryParse(
                                 _configuration["DevLogin:MockLightningIdentity"], out var mockId)
                             && mockId;

        // Read token expiry from config (defaults to sensible values)
        _adminTokenExpiryHours = _configuration.GetValue<int>("TokenExpiry:AdminHours", 720); // 30 days
        _developerTokenExpiryHours = _configuration.GetValue<int>("TokenExpiry:DeveloperHours", 2);
        _defaultTokenExpiryMinutes = _configuration.GetValue<int>("TokenExpiry:DefaultMinutes", 30);

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
    public async Task<InvoiceResponse> CreateInvoice(string userId, long amountSats, string memo, Project? project = null)
    {
        if (_useMock)
        {
            return new InvoiceResponse
            {
                RHash = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                PaymentRequest = $"lnmock1invoice{Guid.NewGuid():N}",
                AddIndex = "0",
                PaymentAddr = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                Settled = false
            };
        }

        var (baseUrl, macaroonHex) = GetEffectiveLndConfig(project);
        
        // Set macaroon for this request
        if (!string.IsNullOrWhiteSpace(macaroonHex))
        {
            SetMacaroonHeader(macaroonHex);
        }
        else
        {
            await EnsureMacaroonHeaderAsync();
        }

        var url = $"{baseUrl}/v1/invoices";

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
    /// CENTRALIZED: Create invoice and return hex payment hash.
    /// Use this instead of CreateInvoice to get the payment hash in consistent hex format.
    /// </summary>
    public async Task<InvoiceResult> CreateInvoiceWithHashAsync(
        string userId, 
        long amountSats, 
        string memo,
        int expiryMinutes = 60,
        Project? project = null)
    {
        var invoice = await CreateInvoice(userId, amountSats, memo, project);
        
        // CENTRALIZED: Convert base64 r_hash to hex once - never use invoice.RHash directly!
        var rHashBytes = Convert.FromBase64String(invoice.RHash);
        var rHashHex = Convert.ToHexString(rHashBytes).ToLowerInvariant();
        
        var expiresAtUnix = DateTimeOffset.UtcNow.AddMinutes(expiryMinutes).ToUnixTimeSeconds();
        
        return new InvoiceResult
        {
            PaymentHash = rHashHex,  // Always 64-char hex
            Bolt11 = invoice.PaymentRequest,
            AmountSats = amountSats,
            ExpiresAtUnix = expiresAtUnix
        };
    }

    /// <summary>
    /// NEW: Create a login-specific invoice for dev auth flow.
    /// Wraps LND /v1/invoices and returns an object with Id + BOLT11 string.
    /// </summary>
    public async Task<LoginInvoiceResult> CreateLoginInvoiceAsync(
        string email,
        long amountSats,
        int expiryMinutes,
        Project? project = null)
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

        var (baseUrl, macaroonHex) = GetEffectiveLndConfig(project);
        
        // Set macaroon for this request
        if (!string.IsNullOrWhiteSpace(macaroonHex))
        {
            SetMacaroonHeader(macaroonHex);
        }
        else
        {
            await EnsureMacaroonHeaderAsync();
        }

        var url = $"{baseUrl}/v1/invoices";

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

        // LND returns r_hash as base64 (32 bytes → ~44 chars). Convert to hex for storage/lookup.
        var rHashBytes = Convert.FromBase64String(invoice.RHash);
        var rHashHex = Convert.ToHexString(rHashBytes).ToLowerInvariant();

        return new LoginInvoiceResult
        {
            // Store as hex (64 chars) for consistent lookups
            InvoiceId = rHashHex,
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
    public async Task<InvoiceStatusResult> GetInvoiceStatusAsync(string paymentHashB64, Project? project = null)
    {
        // DEBUG
        Console.WriteLine($"[DEBUG] GetInvoiceStatusAsync called with: '{paymentHashB64}' (length={paymentHashB64?.Length})");
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

        var (baseUrl, macaroonHex) = GetEffectiveLndConfig(project);
        
        // Set macaroon for this request
        if (!string.IsNullOrWhiteSpace(macaroonHex))
        {
            SetMacaroonHeader(macaroonHex);
        }
        else
        {
            await EnsureMacaroonHeaderAsync();
        }

        paymentHashB64 = paymentHashB64.Trim();

        // Normalize 32-byte payment hash
        if (!TryNormalizePaymentHash(paymentHashB64, out var rHashBytes, out var error))
        {
            Console.WriteLine($"[DEBUG] TryNormalizePaymentHash failed: {error}");
            return new InvoiceStatusResult
            {
                IsPaid = false,
                PayerLightningAuthKey = null
            };
        }

        var hex = Convert.ToHexString(rHashBytes!).ToLowerInvariant();
        var url = $"{_baseUrl}/v1/invoice/{hex}";
        Console.WriteLine($"[DEBUG] Calling LND: {url}");
        var response = await _httpClient.GetAsync(url);
        Console.WriteLine($"[DEBUG] LND response status: {response.StatusCode}");

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            Console.WriteLine($"[DEBUG] Invoice not found in LND");
            return new InvoiceStatusResult
            {
                IsPaid = false,
                PayerLightningAuthKey = null
            };
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[DEBUG] LND error: {body}");

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
        Console.WriteLine($"[DEBUG] LND response body: {json}");
        var invoice = JsonSerializer.Deserialize<InvoiceResponse>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );

        bool isSettled = invoice?.Settled == true;
        Console.WriteLine($"[DEBUG] Settled: {isSettled}, invoice.Settled: {invoice?.Settled}");

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
    /// Get LND node information for health checks.
    /// </summary>
    public async Task<LndInfo> GetLndInfoAsync(CancellationToken ct)
    {
        return await GetLndInfoWithConfigAsync(null, null, ct);
    }

    /// <summary>
    /// Test LND connection with custom config.
    /// </summary>
    public async Task<LndInfo> TestConnectionAsync(string baseUrl, string? macaroonHex, CancellationToken ct)
    {
        return await GetLndInfoWithConfigAsync(baseUrl, macaroonHex, ct);
    }

    private async Task<LndInfo> GetLndInfoWithConfigAsync(string? customBaseUrl, string? customMacaroonHex, CancellationToken ct)
    {
        var (baseUrl, macaroonHex) = GetEffectiveLndConfig(null);
        
        // Override with custom values if provided
        if (!string.IsNullOrWhiteSpace(customBaseUrl))
            baseUrl = customBaseUrl.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(customMacaroonHex))
            macaroonHex = NormalizeMacaroonToHex(customMacaroonHex.Trim());

        if (_useMock)
        {
            return new LndInfo
            {
                Version = "mock",
                BlockHeight = 0,
                NumActiveChannels = 0,
                NumPeers = 0
            };
        }

        // Set custom macaroon for this request
        if (!string.IsNullOrWhiteSpace(macaroonHex))
        {
            SetMacaroonHeader(macaroonHex);
        }
        else
        {
            await EnsureMacaroonHeaderAsync();
        }

        var response = await _httpClient.GetAsync($"{baseUrl}/v1/getinfo", ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var info = JsonSerializer.Deserialize<LndGetInfoResponse>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return new LndInfo
        {
            Version = info?.Version ?? "unknown",
            BlockHeight = info?.BlockHeight ?? 0,
            NumActiveChannels = info?.NumActiveChannels ?? 0,
            NumPeers = info?.NumPeers ?? 0
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
    public async Task<bool> CheckPaymentStatus(string paymentHashB64, Project? project = null)
    {
        var status = await GetInvoiceStatusAsync(paymentHashB64, project);
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
            expiresUtc: DateTime.UtcNow.AddMinutes(_defaultTokenExpiryMinutes)
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
            expiresUtc: expiresUtc ?? DateTime.UtcNow.AddMinutes(_defaultTokenExpiryMinutes)
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

    public async Task<(string PaymentHash, string Preimage)> PayInvoice(string invoice, Project? project = null)
    {
        var (baseUrl, macaroonHex) = GetEffectiveLndConfig(project);
        
        // Set macaroon for this request
        if (!string.IsNullOrWhiteSpace(macaroonHex))
        {
            SetMacaroonHeader(macaroonHex);
        }
        else
        {
            await EnsureMacaroonHeaderAsync();
        }

        try
        {
            var url = $"{baseUrl}/v2/router/send";
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
            expiresUtc: DateTime.UtcNow.AddHours(_adminTokenExpiryHours)
            // Use default audience ("LiveAuthUsers") for compatibility
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
            expiresUtc: DateTime.UtcNow.AddHours(_developerTokenExpiryHours),
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
