namespace LiveAuthCore.Controllers;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
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

[ApiController]
[Route("api/dev/auth")]
public class DevAuthController : ControllerBase
{
    private readonly LiveAuthDbContext _db;
    private readonly LightningService _ln;
    private readonly IConfiguration _config;
    private readonly AuthEventService _authEvents;

    public DevAuthController(
        LiveAuthDbContext db,
        LightningService ln,
        IConfiguration config,
        AuthEventService authEvents)
    {
        _db = db;
        _ln = ln;
        _config = config;
        _authEvents = authEvents;
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
        // DEBUG MODE: Skip Lightning invoice, use demo account
        // Only works in Development environment with #debug in email
        // ─────────────────────────────────────────────
        if (email.Contains("#debug", StringComparison.OrdinalIgnoreCase) 
            && Request.Host.Host.Contains("localhost", StringComparison.OrdinalIgnoreCase))
        {
            var demoDev = await GetOrCreateDemoDeveloperAsync(ct);
            var token = _ln.GenerateJwtToken(demoDev.Id.ToString(), "Developer");
            
            return Ok(new DevStartLoginResponse
            {
                SessionId = Guid.Empty,
                Invoice = "DEBUG_MODE",
                AmountSats = 0,
                ExpiresAtUnix = DateTimeOffset.UtcNow.AddYears(1).ToUnixTimeSeconds(),
                DebugToken = token
            });
        }

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

    private async Task<Developer> GetOrCreateDemoDeveloperAsync(CancellationToken ct)
    {
        var demoEmail = "demo@liveauth.app";
        
        var dev = await _db.Developers
            .FirstOrDefaultAsync(d => d.Email == demoEmail, ct);

        if (dev == null)
        {
            dev = new Developer
            {
                Email = demoEmail,
                GitHubUsername = "demo",
                IsAdmin = true
            };
            _db.Developers.Add(dev);
            await _db.SaveChangesAsync(ct);
        }

        return dev;
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
        
        // DEBUG MODE: Report GitHub as enabled on localhost so login button appears
        var isDebug = Request.Host.Host.Contains("localhost", StringComparison.OrdinalIgnoreCase);
        var enabled = isDebug || !string.IsNullOrWhiteSpace(clientId);

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
    /// Redirect to GitHub for OAuth
    /// </summary>
    [HttpGet("github/start")]
    public async Task<IActionResult> StartGitHubLogin(
        [FromQuery] string? returnUrl = null,
        [FromQuery] bool debug = false,
        CancellationToken ct = default)
    {
        var clientId = _config["GitHub:ClientId"];
        
        // ─────────────────────────────────────────────
        // DEBUG MODE: Skip GitHub OAuth, return demo token
        // Triggered by: debug=true query param OR running locally on localhost
        // ─────────────────────────────────────────────
        if (debug || Request.Host.Host.Contains("localhost", StringComparison.OrdinalIgnoreCase))
        {
            var demoDev = await GetOrCreateDemoDeveloperAsync(ct);
            var token = _ln.GenerateJwtToken(demoDev.Id.ToString(), "Developer");
            
            // Redirect back to the developer portal with token
            var redirectUrl = returnUrl ?? "https://liveauth.app/dev/projects";
            return Redirect($"{redirectUrl}?token={token}");
        }

        if (string.IsNullOrWhiteSpace(clientId))
        {
            return BadRequest("GitHub OAuth not configured.");
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

}
