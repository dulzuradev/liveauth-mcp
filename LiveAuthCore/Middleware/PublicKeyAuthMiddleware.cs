using LiveAuthCore.Data;

namespace LiveAuthCore.Middleware;

using System.Net;
using System.Text.Json;
using LiveAuthCore.Services;

public class PublicKeyAuthMiddleware
{
    private readonly RequestDelegate _next;

    // Browser-safe header
    private const string PublicKeyHeaderName = "X-LW-Public";

    public PublicKeyAuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ApiKeyService apiKeyService,
        BillingService billingService,
        LiveAuthDbContext db)
    {
        if (context.Request.Path.StartsWithSegments("/api/admin", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }
        
        if (HttpMethods.IsOptions(context.Request.Method))
        {
            await _next(context);
            return;
        }
        // Skip auth for these paths
        if (
            context.Request.Path.StartsWithSegments("/api/public/pow", StringComparison.OrdinalIgnoreCase) ||
            context.Request.Path.StartsWithSegments("/api/public/auth", StringComparison.OrdinalIgnoreCase) ||
            context.Request.Path.StartsWithSegments("/api/public/demo", StringComparison.OrdinalIgnoreCase) ||
            context.Request.Path.StartsWithSegments("/api/health", StringComparison.OrdinalIgnoreCase) ||
            context.Request.Path.StartsWithSegments("/api/dev", StringComparison.OrdinalIgnoreCase)
        )
        {
            await _next(context);
            return;
        }

        // For other paths, require API key
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

        var publicKey = values.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(publicKey))
        {
            await WriteError(
                context,
                HttpStatusCode.Unauthorized,
                "invalid_api_key",
                "Public API key cannot be empty."
            );
            return;
        }

        // Authenticate using PUBLIC key
        var authResult = await apiKeyService
            .AuthenticatePublicKeyAsync(publicKey, context.RequestAborted);

        switch (authResult.Status)
        {
            case ApiKeyAuthStatus.Ok:
                var project = authResult.Project;

                try
                {
                    var changed = billingService.EnsurePlanIsCurrent(project, DateTime.UtcNow);
                    if (changed)
                    {
                        db.Projects.Update(project);
                        await db.SaveChangesAsync(context.RequestAborted);
                    }
                }
                catch
                {
                    // do NOT kill request for billing sync failure
                }

                context.Items["LW_Project"] = project;
                context.Items[HttpContextKeys.Project] = project;

                await _next(context);
                break;

            case ApiKeyAuthStatus.Revoked:
                await WriteError(
                    context,
                    HttpStatusCode.Forbidden,
                    "api_key_revoked",
                    "This public API key has been revoked."
                );
                break;

            case ApiKeyAuthStatus.Invalid:
            default:
                await WriteError(
                    context,
                    HttpStatusCode.Unauthorized,
                    "invalid_api_key",
                    "Invalid public API key."
                );
                break;
        }
    }

    private static async Task WriteError(
        HttpContext context,
        HttpStatusCode status,
        string code,
        string message)
    {
        context.Response.StatusCode = (int)status;
        context.Response.ContentType = "application/json";

        var payload = new
        {
            error = code,
            error_description = message
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}