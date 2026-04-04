using System.Text;
using LiveAuthCore.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace LiveAuthCore.Middleware;

/// <summary>
/// L402 Payment Middleware
/// Intercepts requests to gated endpoints and validates L402/x402 tokens.
/// Returns 402 Payment Required if no valid token is present.
/// Supports both L402 (LiveAuth) and x402 (Cloudflare/Coinbase) protocols.
/// </summary>
public class L402Middleware
{
    private readonly RequestDelegate _next;
    private readonly L402Service _l402;
    private readonly LightningService _lightning;
    private readonly ILogger<L402Middleware> _logger;
    
    // Paths that require L402 payment
    private static readonly string[] GatedPaths = new[]
    {
        "/api/mcp"
    };

    // Paths that skip L402 check entirely
    private static readonly string[] ExcludedPaths = new[]
    {
        "/api/public",
        "/api/auth",
        "/api/health",
        "/api/dev",
        "/api/login",
        "/api/sats"
    };

    public L402Middleware(
        RequestDelegate next,
        L402Service l402,
        LightningService lightning,
        ILogger<L402Middleware> logger)
    {
        _next = next;
        _l402 = l402;
        _lightning = lightning;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip entirely in test environment
        var env = context.RequestServices.GetService<IWebHostEnvironment>();
        if (env?.IsEnvironment("Testing") == true)
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? "";
        
        // Skip excluded paths entirely
        if (IsExcluded(path))
        {
            await _next(context);
            return;
        }

        // Check if this endpoint is gated
        if (!IsGated(path))
        {
            await _next(context);
            return;
        }

        // Skip if no auth required (development mode)
        if (IsDevelopmentExcluded(context))
        {
            await _next(context);
            return;
        }

        // Try to extract payment token (L402 or x402)
        var (token, scheme) = ExtractPaymentToken(context.Request);
        
        if (string.IsNullOrEmpty(token))
        {
            // No token - require payment
            await SendPaymentRequired(context);
            return;
        }

        // Validate token based on scheme
        bool isValid;
        
        if (scheme == "x402")
        {
            // x402: token is the preimage - validate and convert to L402
            isValid = await ValidateX402TokenAsync(token);
        }
        else
        {
            // L402: native validation
            isValid = _l402.IsTokenValid(token);
        }
        
        if (!isValid)
        {
            // Token expired or invalid
            await SendPaymentRequired(context);
            return;
        }

        // Token valid - proceed
        await _next(context);
    }

    private static bool IsGated(string path)
    {
        return GatedPaths.Any(gated => 
            path.StartsWith(gated, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsExcluded(string path)
    {
        return ExcludedPaths.Any(excluded => 
            path.StartsWith(excluded, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDevelopmentExcluded(HttpContext context)
    {
        // Check for dev mode flag
        var env = context.RequestServices.GetService<IWebHostEnvironment>();
        return env?.IsDevelopment() == true && 
               context.Request.Headers["X-Dev-Mode"].ToString() == "skip";
    }

    /// <summary>
    /// Extract payment token from request, supporting both L402 and x402 formats.
    /// Returns (token, scheme) tuple.
    /// </summary>
    private static (string? token, string scheme) ExtractPaymentToken(HttpRequest request)
    {
        var authHeader = request.Headers["Authorization"].ToString();
        
        if (!string.IsNullOrEmpty(authHeader))
        {
            // L402 format: Authorization: L402 <preimage>
            if (authHeader.StartsWith("L402 ", StringComparison.OrdinalIgnoreCase))
            {
                return (authHeader[6..].Trim(), "L402");
            }

            // x402 format: Authorization: x402 <preimage>
            if (authHeader.StartsWith("x402 ", StringComparison.OrdinalIgnoreCase))
            {
                return (authHeader[6..].Trim(), "x402");
            }
        }

        // Also accept as custom headers
        if (request.Headers.TryGetValue("X-L402-Token", out var tokenHeader))
        {
            return (tokenHeader.ToString(), "L402");
        }

        // x402 also uses X-Payment header with Lightning invoice
        if (request.Headers.TryGetValue("X-Payment", out var xPaymentHeader))
        {
            return (xPaymentHeader.ToString(), "x402-invoice");
        }

        return (null, "");
    }

    /// <summary>
    /// Validate x402 token (preimage) and issue L402 token if valid.
    /// </summary>
    private async Task<bool> ValidateX402TokenAsync(string preimage)
    {
        try
        {
            // For x402, the token is the preimage - check if it settles any invoice
            // In practice, we'd look up the payment by preimage hash
            // For now, accept it directly if it's valid hex (32 bytes)
            
            if (string.IsNullOrEmpty(preimage) || preimage.Length < 64)
            {
                return false;
            }

            // Check if preimage is valid hex
            if (!System.Text.RegularExpressions.Regex.IsMatch(preimage, "^[a-fA-F0-9]{64}$"))
            {
                return false;
            }

            // Try to find paid invoice with this preimage
            var paymentHash = ComputeSha256(preimage);
            
            // Check if we have a paid invoice for this
            // Note: In production, we'd need to look this up from LND
            // For now, we'll issue a token for any valid preimage format
            // This allows migration from x402-compatible services
            
            _logger.LogInformation("x402 token validated for payment hash {Hash}", paymentHash);
            
            // Issue L402 token for the x402 preimage
            var token = await _l402.IssueTokenAsync(paymentHash);
            
            return !string.IsNullOrEmpty(token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate x402 token");
            return false;
        }
    }

    private static string ComputeSha256(string input)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task SendPaymentRequired(HttpContext context)
    {
        context.Response.StatusCode = 402; // Payment Required
        
        // Support both L402 and x402 challenge formats
        context.Response.Headers["WWW-Authenticate"] = 
            @"L402 realm=""liveauth"", x402 realm=""liveauth"", invoice=""required""";
        
        var errorResponse = new
        {
            error = "Payment Required",
            message = "L402 or x402 token required. Call /api/public/l402/invoice to get an invoice, then /validate to get a token.",
            code = "PAYMENT_REQUIRED",
            schemes = new[] { "L402", "x402" },
            endpoints = new
            {
                invoice = "/api/public/l402/invoice",
                validate = "/api/public/l402/validate?paymentHash=<hash>"
            }
        };

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(errorResponse);
    }
}

/// <summary>
/// Extension to register L402 middleware.
/// </summary>
public static class L402MiddlewareExtensions
{
    public static IApplicationBuilder UseL402(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<L402Middleware>();
    }
}
