namespace LiveAuthCore.Controllers;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Models;
using LiveAuthCore.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using AspNet.Security.OAuth.GitHub;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text.Json;
using Microsoft.AspNetCore.RateLimiting;

[ApiController]
[Route("api/dev/auth")]
public class DevAuthController : ControllerBase
{
    private readonly LiveAuthDbContext _db;
    private readonly LightningService _ln;
    private readonly IConfiguration _config;
    private readonly AuthEventService _authEvents;
    private readonly EmailService _email;

    public DevAuthController(
        LiveAuthDbContext db,
        LightningService ln,
        IConfiguration config,
        AuthEventService authEvents,
        EmailService email)
    {
        _db = db;
        _ln = ln;
        _config = config;
        _authEvents = authEvents;
        _email = email;
    }

    // POST /api/dev/auth/start
    [HttpPost("start")]
    public async Task<ActionResult<DevStartLoginResponse>> StartLogin(
        [FromBody] DevStartLoginRequest request,
        CancellationToken ct)
    {
        var email = (request.DeveloperEmail ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest("Developer email is required.");

        // ─────────────────────────────────────────────
        // Amount sats & expiry from config (defaults if missing)
        // ─────────────────────────────────────────────
        var amountSats =
            _config.GetValue<long?>("DevLogin:AmountSats")
            ?? 21L;

        if (amountSats < 0)
            amountSats = 0;

        var expiryMinutes =
            _config.GetValue<int?>("DevLogin:ExpiryMinutes")
            ?? 10;

        if (expiryMinutes <= 0)
            expiryMinutes = 10;

        // ─────────────────────────────────────────────
        // Create Lightning invoice for the login request
        // (uses real LND, or mock if enabled)
        // ─────────────────────────────────────────────
        var invoiceResult =
            await _ln.CreateLoginInvoiceAsync(email, amountSats, expiryMinutes);

        var session = new DevLoginSession
        {
            Id = Guid.NewGuid(),
            Email = email,

            // r_hash (base64)
            InvoiceId     = invoiceResult.InvoiceId,
            InvoiceBolt11 = invoiceResult.Bolt11,

            AmountSats = amountSats,
            ExpiresAt  = DateTime.UtcNow.AddMinutes(expiryMinutes),
            IsPaid     = false
        };

        _db.DevLoginSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        return Ok(new DevStartLoginResponse
        {
            SessionId     = session.Id,
            Invoice       = session.InvoiceBolt11,
            AmountSats    = amountSats,
            ExpiresAtUnix = new DateTimeOffset(session.ExpiresAt).ToUnixTimeSeconds()
        });
    }


    // POST /api/dev/auth/confirm
    [HttpPost("confirm")]
    public async Task<ActionResult<DevConfirmLoginResponse>> ConfirmLogin(
        [FromBody] DevConfirmLoginRequest request)
    {
        var session = await _db.DevLoginSessions
            .SingleOrDefaultAsync(s => s.Id == request.SessionId);

        if (session == null)
        {
            return Ok(new DevConfirmLoginResponse
            {
                Verified = false,
                Token = null
            });
        }

        if (DateTime.UtcNow > session.ExpiresAt)
        {
            return Ok(new DevConfirmLoginResponse
            {
                Verified = false,
                Token = null
            });
        }

        // Query Lightning node: has this invoice been paid?
        var status = await _ln.GetInvoiceStatusAsync(session.InvoiceId);

        if (!status.IsPaid)
        {
            return Ok(new DevConfirmLoginResponse
            {
                Verified = false,
                Token = null
            });
        }

        // Mark session as paid (idempotent)
        if (!session.IsPaid)
        {
            session.IsPaid = true;
            session.PaidAt = DateTime.UtcNow;
            session.PayerLightningAuthKey = status.PayerLightningAuthKey;
            await _db.SaveChangesAsync();
        }

        var email = session.Email.Trim();
        var payerKey = status.PayerLightningAuthKey;

        Developer dev;

        // --------------------------------------------------------------------
        // FUTURE-PROOF PATH: when payerKey is available (LNURL-auth, etc.)
        // --------------------------------------------------------------------
        if (!string.IsNullOrWhiteSpace(payerKey))
        {
            var devByKey = await _db.Developers
                .SingleOrDefaultAsync(d => d.LightningAuthKey == payerKey);

            var devByEmail = await _db.Developers
                .SingleOrDefaultAsync(d => d.Email == email);

            // CASE 1: no devs yet; first-time login
            if (devByKey == null && devByEmail == null)
            {
                dev = new Developer
                {
                    Id = Guid.NewGuid(),
                    Email = email,
                    LightningAuthKey = payerKey
                };
                _db.Developers.Add(dev);
                await _db.SaveChangesAsync();
            }
            // CASE 2: we know this Lightning key, but email is new or changed
            else if (devByKey != null)
            {
                dev = devByKey;

                if (!string.Equals(dev.Email, email, StringComparison.OrdinalIgnoreCase))
                {
                    // optionally allow updating email to latest
                    dev.Email = email;
                    await _db.SaveChangesAsync();
                }
            }
            // CASE 3: email exists but belongs to a different Lightning identity
            else // devByKey == null && devByEmail != null
            {
                if (string.IsNullOrWhiteSpace(devByEmail!.LightningAuthKey))
                {
                    // old account with no bound Lightning key: bind now
                    devByEmail.LightningAuthKey = payerKey;
                    await _db.SaveChangesAsync();
                    dev = devByEmail;
                }
                else if (devByEmail.LightningAuthKey == payerKey)
                {
                    dev = devByEmail;
                }
                else
                {
                    // 🚫 EMAIL HIJACK ATTEMPT:
                    // Someone is trying to login with an email already claimed
                    // by a different Lightning identity.
                    return Ok(new DevConfirmLoginResponse
                    {
                        Verified = false,
                        Token = null
                    });
                }
            }
        }
        // --------------------------------------------------------------------
        // CURRENT PATH: no Lightning identity – fallback to email-based dev
        // NOTE: This does NOT protect against email hijack; it matches your
        // current behavior until LNURL-auth is wired.
        // --------------------------------------------------------------------
        else
        {
            var devByEmail = await _db.Developers
                .SingleOrDefaultAsync(d => d.Email == email);

            if (devByEmail == null)
            {
                dev = new Developer
                {
                    Id = Guid.NewGuid(),
                    Email = email,
                    LightningAuthKey = null
                };
                _db.Developers.Add(dev);
                await _db.SaveChangesAsync();
            }
            else
            {
                dev = devByEmail;
            }
        }

        // Issue JWT with userId claim + Developer role
        var token = GenerateJwtForDeveloper(dev);

        return Ok(new DevConfirmLoginResponse
        {
            Verified = true,
            Token = token
        });
    }

    private string GenerateJwtForDeveloper(Developer dev)
    {
        // Prefer Jwt:SigningKey if present, else fall back to Jwt:Key
        var signingKey = _config["Jwt:SigningKey"] ?? _config["Jwt:Key"];

        if (string.IsNullOrWhiteSpace(signingKey))
            throw new InvalidOperationException(
                "JWT signing key not configured. Set Jwt:SigningKey or Jwt:Key in configuration.");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim("userId", dev.Id.ToString()),
            new Claim(ClaimTypes.Role, "Developer")
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // ============================================================
    // GitHub OAuth Login
    // ============================================================

    /// <summary>
    /// Check if GitHub OAuth is configured
    /// </summary>
    [HttpGet("github/status")]
    public ActionResult<GitHubLoginStatusResponse> GetGitHubStatus()
    {
        var clientId = _config["GitHub:ClientId"];
        var enabled = !string.IsNullOrWhiteSpace(clientId);

        return Ok(new GitHubLoginStatusResponse
        {
            Enabled = enabled
        });
    }

    /// <summary>
    /// GitHub OAuth callback - manually exchange code for token
    /// </summary>
    [HttpGet("github/callback")]
    public async Task<ActionResult<GitHubLoginResponse>> GitHubLogin(
        [FromQuery] string? code,
        [FromQuery] string? state,
        CancellationToken ct)
    {
        try
        {
            // Verify state
            var stateCookie = Request.Cookies["github_oauth_state"];
            if (string.IsNullOrEmpty(stateCookie) || state != stateCookie)
            {
                return BadRequest("Invalid state parameter");
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                return BadRequest("No authorization code provided");
            }

            // Exchange code for access token
            var clientId = _config["GitHub:ClientId"];
            var clientSecret = _config["GitHub:ClientSecret"];
            
            var tokenResponse = await new HttpClient().PostAsync(
                "https://github.com/login/oauth/access_token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "client_id", clientId ?? "" },
                    { "client_secret", clientSecret ?? "" },
                    { "code", code },
                    { "redirect_uri", "https://api.liveauth.app/api/dev/auth/github/callback" }
                }),
                ct);

            var responseContent = await tokenResponse.Content.ReadAsStringAsync(ct);
            
            // Parse the response (it's URL-encoded)
            var parsed = System.Web.HttpUtility.ParseQueryString(responseContent);
            var accessToken = parsed["access_token"];

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return BadRequest("Failed to obtain access token: " + responseContent);
            }

            // Get user info from GitHub
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
            httpClient.DefaultRequestHeaders.Add("User-Agent", "LiveAuth");
            
            var userResponse = await httpClient.GetAsync("https://api.github.com/user", ct);
            var userJson = await userResponse.Content.ReadAsStringAsync(ct);
            
            // Parse user JSON
            var userDoc = System.Text.Json.JsonDocument.Parse(userJson);
            var githubId = userDoc.RootElement.GetProperty("id").GetInt64().ToString();
            var githubLogin = userDoc.RootElement.GetProperty("login").GetString() ?? "";
            
            // Get email (might need separate call if not public)
            string? email = null;
            try
            {
                var emailResponse = await httpClient.GetAsync("https://api.github.com/user/emails", ct);
                var emailJson = await emailResponse.Content.ReadAsStringAsync(ct);
                var emailDoc = System.Text.Json.JsonDocument.Parse(emailJson);
                foreach (var e in emailDoc.RootElement.EnumerateArray())
                {
                    if (e.GetProperty("primary").GetBoolean() && e.GetProperty("verified").GetBoolean())
                    {
                        email = e.GetProperty("email").GetString();
                        break;
                    }
                }
            }
            catch
            {
                // Email might not be available
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                email = $"{githubLogin}@github";
            }

            // Find or create developer
            var dev = await _db.Developers
                .FirstOrDefaultAsync(d => d.GitHubId == githubId, ct);

            if (dev == null)
            {
                // Check if email already exists (could be Lightning login)
                dev = await _db.Developers
                    .FirstOrDefaultAsync(d => d.Email == email, ct);

                if (dev != null)
                {
                    // Link GitHub to existing account
                    dev.GitHubId = githubId;
                    dev.GitHubUsername = githubLogin;
                }
                else
                {
                    // Create new developer
                    dev = new Developer
                    {
                        Id = Guid.NewGuid(),
                        Email = email,
                        GitHubId = githubId,
                        GitHubUsername = githubLogin
                    };
                    _db.Developers.Add(dev);
                }

                await _db.SaveChangesAsync(ct);
            }
            else
            {
                // Update username in case it changed
                dev.GitHubUsername = githubLogin;
                await _db.SaveChangesAsync(ct);
            }

            // Issue JWT
            var token = GenerateJwtForDeveloper(dev);

            // Clear the state cookie
            Response.Cookies.Delete("github_oauth_state");

            // Redirect to frontend with token
            var frontendUrl = _config["App:FrontendUrl"] ?? "https://liveauth.app";
            return Redirect($"{frontendUrl}/dev/projects?token={token}");
        }
        catch (Exception ex)
        {
            return StatusCode(500, "Error: " + ex.Message);
        }
    }

    /// <summary>
    /// Redirect to GitHub for OAuth, or bypass to dev account in local dev mode.
    /// </summary>
    [HttpGet("github/start")]
    public async Task<IActionResult> StartGitHubLogin(
        [FromQuery] bool? dev,
        CancellationToken ct)
    {
        // Dev bypass: create/get local dev account without GitHub OAuth
        // Use ?dev=true or when GitHub is not configured in development
        var isDevBypass = dev == true;
        var clientId = _config["GitHub:ClientId"];
        var isDevEnvironment = _config["ASPNETCORE_ENVIRONMENT"] == "Development";

        if (isDevBypass || (isDevEnvironment && string.IsNullOrWhiteSpace(clientId)))
        {
            return await CreateDevBypassLogin(ct);
        }

        if (string.IsNullOrWhiteSpace(clientId))
        {
            return BadRequest(new { error = "GitHub OAuth not configured." });
        }

        // Generate state for CSRF protection
        var state = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        
        // Build the authorization URL with https callback
        var callbackUrl = $"https://api.liveauth.app/api/dev/auth/github/callback";
        var authUrl = $"https://github.com/login/oauth/authorize" +
            $"?client_id={clientId}" +
            $"&redirect_uri={Uri.EscapeDataString(callbackUrl)}" +
            $"&scope={Uri.EscapeDataString("user:email")}" +
            $"&state={state}";

        // Store state in cookie for verification later
        Response.Cookies.Append("github_oauth_state", state, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Expires = DateTimeOffset.UtcNow.AddMinutes(10)
        });

        return Redirect(authUrl);
    }

    // ============================================================
    // Email/Password Auth
    // ============================================================

    /// <summary>
    /// Register a new developer account with email + password.
    /// POST /api/dev/auth/register
    /// </summary>
    [HttpPost("register")]
    [EnableRateLimiting("auth:x10")]
    public async Task<ActionResult<RegisterResponse>> Register(
        [FromBody] RegisterRequest request,
        CancellationToken ct)
    {
        var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        var password = request.Password ?? string.Empty;

        if (string.IsNullOrWhiteSpace(email) || !IsValidEmail(email))
            return BadRequest(new { error = "Valid email is required." });

        if (password.Length < 12)
            return BadRequest(new { error = "Password must be at least 12 characters." });

        // Check if email already exists (any provider)
        var existing = await _db.Developers
            .FirstOrDefaultAsync(d => d.Email == email, ct);

        if (existing != null)
        {
            // If existing account has no password set, they may have used GitHub/LN auth
            // We won't overwrite — tell them to use existing method
            if (string.IsNullOrWhiteSpace(existing.PasswordHash))
            {
                return Conflict(new { error = "An account exists with this email but uses a different login method." });
            }

            return Conflict(new { error = "An account with this email already exists." });
        }

        var (hash, salt) = HashPasswordWithSalt(password);
        var verificationToken = Guid.NewGuid().ToString("N");

        var dev = new Developer
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = hash,
            PasswordSalt = salt,
            VerificationToken = verificationToken,
            VerificationExpiresAt = DateTime.UtcNow.AddHours(24),
            EmailVerified = false
        };

        _db.Developers.Add(dev);
        await _db.SaveChangesAsync(ct);

        _authEvents.Log(dev.Id, "register_email", true, reason: "EMAIL_AUTH_REGISTER");

        // Send verification email
        var emailSent = await _email.SendVerificationEmailAsync(email, verificationToken);

        return Ok(new RegisterResponse
        {
            DeveloperId = dev.Id,
            Message = emailSent
                ? "Registration successful. Check your email to verify your address."
                : "Registration successful. Note: email delivery may be unavailable.",
            EmailVerificationRequired = true,
            EmailSent = emailSent
        });
    }

    /// <summary>
    /// Verify email address using token from registration email.
    /// POST /api/dev/auth/verify-email
    /// </summary>
    [HttpPost("verify-email")]
    [AllowAnonymous]
    public async Task<ActionResult<VerifyEmailResponse>> VerifyEmail(
        [FromBody] VerifyEmailRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            return BadRequest(new { error = "Token is required." });

        var dev = await _db.Developers
            .FirstOrDefaultAsync(d => d.VerificationToken == request.Token, ct);

        if (dev == null)
            return NotFound(new { error = "Invalid or expired verification token." });

        if (dev.VerificationExpiresAt.HasValue && dev.VerificationExpiresAt.Value < DateTime.UtcNow)
            return BadRequest(new { error = "Verification token has expired. Please request a new one." });

        dev.EmailVerified = true;
        dev.VerificationToken = null;
        dev.VerificationExpiresAt = null;
        await _db.SaveChangesAsync(ct);

        var token = GenerateJwtForDeveloper(dev);

        _authEvents.Log(dev.Id, "verify_email", true, reason: "EMAIL_VERIFIED");

        return Ok(new VerifyEmailResponse
        {
            Success = true,
            Token = token,
            Message = "Email verified successfully."
        });
    }

    /// <summary>
    /// Login with email + password.
    /// POST /api/dev/auth/login
    /// </summary>
    [HttpPost("login")]
    [EnableRateLimiting("auth:x10")]
    public async Task<ActionResult<LoginResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken ct)
    {
        var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        var password = request.Password ?? string.Empty;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return Unauthorized(new { error = "Email and password are required." });

        var dev = await _db.Developers
            .FirstOrDefaultAsync(d => d.Email == email, ct);

        if (dev == null)
        {
            _authEvents.Log(null, "login_email", false, reason: "USER_NOT_FOUND");
            return Unauthorized(new { error = "Invalid credentials." });
        }

        if (string.IsNullOrWhiteSpace(dev.PasswordHash) || string.IsNullOrWhiteSpace(dev.PasswordSalt))
            return Unauthorized(new { error = "This account uses a different login method." });

        var hash = HashPassword(password, dev.PasswordSalt!);
        if (hash != dev.PasswordHash)
        {
            _authEvents.Log(dev.Id, "login_email", false, reason: "BAD_PASSWORD");
            return Unauthorized(new { error = "Invalid credentials." });
        }

        if (!dev.EmailVerified)
            return Unauthorized(new { error = "Please verify your email address before logging in." });

        var token = GenerateJwtForDeveloper(dev);

        _authEvents.Log(dev.Id, "login_email", true, reason: "EMAIL_AUTH_LOGIN");

        return Ok(new LoginResponse
        {
            Verified = true,
            Token = token,
            Message = "Login successful."
        });
    }

    // ============================================================
    // Dev bypass login (local dev only)
    // ============================================================

    /// <summary>
    /// Create or get a local dev account for bypass login.
    /// </summary>
    private async Task<IActionResult> CreateDevBypassLogin(CancellationToken ct)
    {
        const string devEmail = "dev@liveauth.local";
        const string devGitHubId = "dev-local-001";
        const string devUsername = "dev-local";

        // Find or create dev account
        var dev = await _db.Developers
            .FirstOrDefaultAsync(d => d.Email == devEmail, ct);

        if (dev == null)
        {
            dev = new Developer
            {
                Id = Guid.NewGuid(),
                Email = devEmail,
                GitHubId = devGitHubId,
                GitHubUsername = devUsername,
                CreatedAt = DateTime.UtcNow
            };
            _db.Developers.Add(dev);
            await _db.SaveChangesAsync(ct);
        }

        // Generate JWT
        var token = GenerateJwtForDeveloper(dev);

        // Build redirect URL
        var frontendUrl = _config["App:FrontendUrl"] ?? "http://localhost:4200";
        var redirectUrl = $"{frontendUrl}/dev/projects?token={token}";

        return Ok(new { redirectUrl });
    }

    // ============================================================
    // Password Hashing Utilities
    // ============================================================

    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }

    private static (string Hash, string Salt) HashPasswordWithSalt(string password)
    {
        var saltBytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(saltBytes);
        var salt = Convert.ToBase64String(saltBytes);

        var hash = HashPassword(password, salt);
        return (hash, salt);
    }

    /// <summary>
    /// Developer logout - clears the GitHub OAuth state cookie to prevent
    /// "invalid state parameter" errors on next login after logout.
    /// </summary>
    [HttpPost("logout")]
    public ActionResult Logout()
    {
        // Clear the GitHub OAuth state cookie
        Response.Cookies.Delete("github_oauth_state");
        return Ok(new { success = true });
    }

    private static string HashPassword(string password, string salt)
    {
        var saltBytes = Convert.FromBase64String(salt);
        using var pbkdf2 = new Rfc2898DeriveBytes(password, saltBytes, 100_000, HashAlgorithmName.SHA256);
        return Convert.ToBase64String(pbkdf2.GetBytes(32));
    }

}
