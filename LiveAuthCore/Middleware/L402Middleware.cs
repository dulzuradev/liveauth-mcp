using System.Text;
using LiveAuthCore.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace LiveAuthCore.Middleware;

/// <summary>
/// L402 Payment Middleware
/// Intercepts requests to gated endpoints and validates L402 tokens.
/// Returns 402 Payment Required if no valid token is present.
/// </summary>
public class L402Middleware
{
    private readonly RequestDelegate _next;
    private readonly L402Service _l402;
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
        "/api/login"
    };

    public L402Middleware(
        RequestDelegate next,
        L402Service l402,
        ILogger<L402Middleware> logger)
    {
        _next = next;
        _l402 = l402;
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

        // Try to extract L402 token
        var token = ExtractL402Token(context.Request);
        
        if (string.IsNullOrEmpty(token))
        {
            // No token - require payment
            await SendPaymentRequired(context);
            return;
        }

        // Validate token
        if (!_l402.IsTokenValid(token))
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

    private static string? ExtractL402Token(HttpRequest request)
    {
        // L402 format: Authorization: L402 <preimage>
        var authHeader = request.Headers["Authorization"].ToString();
        
        if (string.IsNullOrEmpty(authHeader))
            return null;

        if (authHeader.StartsWith("L402 ", StringComparison.OrdinalIgnoreCase))
        {
            return authHeader[6..].Trim(); // Everything after "L402 "
        }

        // Also accept as custom header
        if (request.Headers.TryGetValue("X-L402-Token", out var tokenHeader))
        {
            return tokenHeader.ToString();
        }

        return null;
    }

    private static async Task SendPaymentRequired(HttpContext context)
    {
        context.Response.StatusCode = 402; // Payment Required
        context.Response.Headers["WWW-Authenticate"] = @"L402 realm=""liveauth"", invoice=""required""";
        
        var errorResponse = new
        {
            error = "Payment Required",
            message = "L402 token required. Call /api/public/l402/invoice to get an invoice, then /validate to get a token.",
            code = "L402_PAYMENT_REQUIRED"
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
