namespace LiveAuthCore.Middleware;

using System.Net;
using System.Text.Json;
using LiveAuthCore.Services;

public class ApiKeyAuthMiddleware
{
    private readonly RequestDelegate _next;

    // Customize the header name if you want (e.g. "X-LW-Secret")
    private const string ApiKeyHeaderName = "X-LW-Secret";
    
    private const string PublicKeyHeaderName = "X-LW-Public";


    public ApiKeyAuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ApiKeyService apiKeyService)
    {
        if (context.Request.Path.StartsWithSegments("/api/admin", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Allow CORS preflight
        if (HttpMethods.IsOptions(context.Request.Method))
        {
            await _next(context);
            return;
        }

        // Only apply to public verification endpoints
        if (
            !context.Request.Path.StartsWithSegments("/api/public/pow", StringComparison.OrdinalIgnoreCase) &&
            !context.Request.Path.StartsWithSegments("/api/public/auth", StringComparison.OrdinalIgnoreCase)
        )
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(PublicKeyHeaderName, out var values))
        {
            await WriteError(
                context,
                HttpStatusCode.Unauthorized,
                "missing_api_key",
                "Missing public API key header."
            );
            return;
        }

        var publicKey = values.FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(publicKey))
        {
            await WriteError(
                context,
                HttpStatusCode.Unauthorized,
                "invalid_api_key",
                "Public API key cannot be empty."
            );
            return;
        }

        var authResult = await apiKeyService
            .AuthenticatePublicKeyAsync(publicKey, context.RequestAborted);

        switch (authResult.Status)
        {
            case ApiKeyAuthStatus.Ok:
                context.Items[HttpContextKeys.Project] = authResult.Project;
                await _next(context);
                return;

            case ApiKeyAuthStatus.Revoked:
                await WriteError(
                    context,
                    HttpStatusCode.Forbidden,
                    "api_key_revoked",
                    "This public API key has been revoked."
                );
                return;

            default:
                await WriteError(
                    context,
                    HttpStatusCode.Unauthorized,
                    "invalid_api_key",
                    "Invalid public API key."
                );
                return;
        }
    }

    private static async Task WriteError(HttpContext context, HttpStatusCode status, string code, string message)
    {
        context.Response.StatusCode = (int)status;
        context.Response.ContentType = "application/json";
        context.Response.Headers["Cache-Control"] = "no-store";

        var obj = new
        {
            error = code,
            error_description = message
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(obj));
    }
}

public static class HttpContextKeys
{
    public const string Project = "LW_Project";
}