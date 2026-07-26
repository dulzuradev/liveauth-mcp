using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace LiveAuth.CostShield.AspNetCore;

/// <summary>Authorization filter created by <see cref="LiveAuthProtectedAttribute"/>.</summary>
public sealed class LiveAuthCostShieldAuthorizationFilter
    : IAsyncAuthorizationFilter
{
    private readonly ILiveAuthCostShieldVerifier _verifier;
    private readonly string _action;
    private readonly string? _origin;
    private readonly LiveAuthCostShieldConsumeMode _consume;

    /// <summary>Creates an authorization filter for a protected action.</summary>
    public LiveAuthCostShieldAuthorizationFilter(
        ILiveAuthCostShieldVerifier verifier,
        string action,
        string? origin,
        LiveAuthCostShieldConsumeMode consume)
    {
        _verifier = verifier;
        _action = action;
        _origin = origin;
        _consume = consume;
    }

    /// <inheritdoc />
    public async Task OnAuthorizationAsync(
        AuthorizationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var token = ReadBearerToken(
            context.HttpContext.Request.Headers.Authorization);
        if (token == null)
        {
            context.Result = ErrorResult(
                HttpStatusCode.Unauthorized,
                "missing_authorization",
                "Provide a CostShield bearer token in Authorization.");
            return;
        }

        try
        {
            var authorization = await _verifier.AuthorizeAsync(
                token,
                _action,
                _origin,
                _consume,
                context.HttpContext.RequestAborted);
            context.HttpContext.Features.Set<
                ILiveAuthCostShieldFeature>(
                new LiveAuthCostShieldFeature(authorization));
        }
        catch (LiveAuthCostShieldException exception)
        {
            context.Result = ErrorResult(
                ResolveStatusCode(exception),
                exception.Code,
                exception.Message);
        }
    }

    private static string? ReadBearerToken(string? header)
    {
        const string prefix = "Bearer ";
        if (string.IsNullOrWhiteSpace(header) ||
            !header.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = header[prefix.Length..].Trim();
        return token.Length == 0 || token.Contains(' ')
            ? null
            : token;
    }

    private static HttpStatusCode ResolveStatusCode(
        LiveAuthCostShieldException exception)
    {
        if (exception.StatusCode is { } statusCode)
            return statusCode;

        return exception.Code switch
        {
            "missing_secret_key" or
            "single_use_requires_consumption" =>
                HttpStatusCode.InternalServerError,
            "network_error" or "jwks_unavailable" =>
                HttpStatusCode.ServiceUnavailable,
            _ => HttpStatusCode.Unauthorized
        };
    }

    private static ObjectResult ErrorResult(
        HttpStatusCode statusCode,
        string code,
        string message)
        => new(new
        {
            error = code,
            error_description = message
        })
        {
            StatusCode = (int)statusCode
        };
}
