using System.Security.Claims;
using System.Text.Encodings.Web;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace LiveAuthCore.Auth;

public class ApiKeyAuthHandler : AuthenticationHandler<ApiKeyAuthOptions>
{
    private readonly ApiKeyService _apiKeys;

    public ApiKeyAuthHandler(
        IOptionsMonitor<ApiKeyAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock,
        ApiKeyService apiKeys
    ) : base(options, logger, encoder, clock)
    {
        _apiKeys = apiKeys;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
            return AuthenticateResult.NoResult();

        var header = authHeader.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var secretKey = header["Bearer ".Length..].Trim();
        var project = await _apiKeys.AuthenticateProjectAsync(secretKey);

        if (project == null)
            return AuthenticateResult.Fail("Invalid API key");

        var claims = new List<Claim>
        {
            new Claim("projectId", project.Id.ToString()),
            new Claim("developerId", project.DeveloperId.ToString()),
            new Claim("plan", project.Plan)
        };

        var identity = new ClaimsIdentity(claims, ApiKeyAuthOptions.SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, ApiKeyAuthOptions.SchemeName);

        return AuthenticateResult.Success(ticket);
    }
}