using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LiveAuth.CostShield.AspNetCore;

internal sealed class LiveAuthCostShieldVerifier
    : ILiveAuthCostShieldVerifier
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IOptions<LiveAuthCostShieldOptions> _options;
    private readonly ICostShieldJwksProvider _jwks;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly JwtSecurityTokenHandler _handler = new()
    {
        MapInboundClaims = false,
        MaximumTokenSizeInBytes =
            LiveAuthCostShieldDefaults.MaximumTokenLength
    };

    public LiveAuthCostShieldVerifier(
        IOptions<LiveAuthCostShieldOptions> options,
        ICostShieldJwksProvider jwks,
        IHttpClientFactory httpClientFactory)
    {
        _options = options;
        _jwks = jwks;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<LiveAuthCostShieldClaims> VerifyAsync(
        string token,
        string action,
        string? origin = null,
        CancellationToken cancellationToken = default)
    {
        ValidateTokenInput(token);
        action = ValidateAction(action);
        var expectedOrigin = origin == null
            ? null
            : NormalizeOrigin(origin);
        var unvalidated = ReadToken(token);
        ValidateHeader(unvalidated);

        ClaimsPrincipal? principal = null;
        SecurityToken? validatedToken = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var keys = await _jwks.GetSigningKeysAsync(
                forceRefresh: attempt > 0,
                cancellationToken);
            try
            {
                principal = _handler.ValidateToken(
                    token,
                    CreateValidationParameters(keys),
                    out validatedToken);
                break;
            }
            catch (SecurityTokenSignatureKeyNotFoundException)
                when (attempt == 0)
            {
                // A signing-key rotation can make a cached JWKS stale.
            }
            catch (SecurityTokenExpiredException exception)
            {
                throw AuthorizationError(
                    "token_expired",
                    "The CostShield token has expired.",
                    exception);
            }
            catch (SecurityTokenNotYetValidException exception)
            {
                throw AuthorizationError(
                    "token_not_active",
                    "The CostShield token is not active yet.",
                    exception);
            }
            catch (SecurityTokenException exception)
            {
                throw AuthorizationError(
                    "invalid_token",
                    "The CostShield token is invalid.",
                    exception);
            }
            catch (ArgumentException exception)
            {
                throw AuthorizationError(
                    "invalid_token",
                    "The CostShield token is invalid.",
                    exception);
            }
        }

        if (principal == null ||
            validatedToken is not JwtSecurityToken validatedJwt)
        {
            throw AuthorizationError(
                "unknown_signing_key",
                "The CostShield token signing key is unknown.");
        }
        ValidateHeader(validatedJwt);
        return ValidateClaims(
            principal,
            action,
            expectedOrigin);
    }

    public async Task<LiveAuthCostShieldAuthorization> AuthorizeAsync(
        string token,
        string action,
        string? origin = null,
        LiveAuthCostShieldConsumeMode consume =
            LiveAuthCostShieldConsumeMode.Auto,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(consume))
        {
            throw ConfigurationError(
                "invalid_consume_mode",
                "consume must be Auto, Always, or Never.");
        }

        var claims = await VerifyAsync(
            token,
            action,
            origin,
            cancellationToken);
        if (consume == LiveAuthCostShieldConsumeMode.Never &&
            claims.SingleUse)
        {
            throw ConfigurationError(
                "single_use_requires_consumption",
                "Single-use CostShield tokens must be consumed remotely.");
        }

        var shouldConsume =
            consume == LiveAuthCostShieldConsumeMode.Always ||
            consume == LiveAuthCostShieldConsumeMode.Auto &&
            claims.SingleUse;
        if (!shouldConsume)
            return new LiveAuthCostShieldAuthorization(claims, null);

        var remote = await ConsumeRemotelyAsync(
            token,
            claims,
            cancellationToken);
        return new LiveAuthCostShieldAuthorization(claims, remote);
    }

    private TokenValidationParameters CreateValidationParameters(
        IReadOnlyList<SecurityKey> signingKeys)
    {
        var options = _options.Value;
        return new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = signingKeys,
            RequireSignedTokens = true,
            ValidateIssuer = true,
            ValidIssuer = options.Issuer,
            ValidateAudience = true,
            ValidAudience = options.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = options.ClockSkew,
            ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 },
            ValidTypes = new[] { LiveAuthCostShieldDefaults.TokenType }
        };
    }

    private LiveAuthCostShieldClaims ValidateClaims(
        ClaimsPrincipal principal,
        string expectedAction,
        string? expectedOrigin)
    {
        var options = _options.Value;
        var tokenId = RequiredClaim(
            principal,
            JwtRegisteredClaimNames.Jti);
        var projectPublicKey = RequiredClaim(
            principal,
            "projectPublicKey");
        var environment = RequiredClaim(principal, "environment");
        var action = RequiredClaim(principal, "action");
        var verificationMethod = RequiredClaim(
            principal,
            "verificationMethod");
        var clientContextHash = RequiredClaim(
            principal,
            "clientContextHash");

        if (!Guid.TryParse(
                RequiredClaim(principal, "projectId"),
                out var projectId) ||
            !Guid.TryParse(
                RequiredClaim(principal, "protectedActionId"),
                out var protectedActionId))
        {
            throw AuthorizationError(
                "invalid_token_claims",
                "The CostShield token has invalid project claims.");
        }

        var expectedEnvironment = options.Environment.ToProtocolValue();
        if (projectId != options.ProjectId)
        {
            throw AuthorizationError(
                "project_mismatch",
                "The token is not valid for this LiveAuth project.",
                statusCode: HttpStatusCode.Forbidden);
        }
        if (!string.Equals(
                environment,
                expectedEnvironment,
                StringComparison.Ordinal))
        {
            throw AuthorizationError(
                "environment_mismatch",
                "The token is not valid for this environment.",
                statusCode: HttpStatusCode.Forbidden);
        }
        if (!string.Equals(
                action,
                expectedAction,
                StringComparison.Ordinal))
        {
            throw AuthorizationError(
                "action_mismatch",
                "The token is not valid for this protected action.",
                statusCode: HttpStatusCode.Forbidden);
        }

        var originClaim = principal.FindFirst("origin")?.Value;
        var normalizedOrigin = originClaim == null
            ? null
            : NormalizeOrigin(originClaim);
        if (expectedOrigin != null &&
            !string.Equals(
                normalizedOrigin,
                expectedOrigin,
                StringComparison.Ordinal))
        {
            throw AuthorizationError(
                "origin_mismatch",
                "The token is not valid for the expected origin.",
                statusCode: HttpStatusCode.Forbidden);
        }

        return new LiveAuthCostShieldClaims(
            tokenId,
            projectId,
            projectPublicKey,
            protectedActionId,
            environment,
            action,
            normalizedOrigin,
            verificationMethod,
            RequiredIntClaim(principal, "difficulty"),
            clientContextHash,
            RequiredBoolClaim(principal, "singleUse"),
            RequiredIntClaim(principal, "configurationVersion"),
            principal.FindFirst("clientSubject")?.Value,
            RequiredLongClaim(
                principal,
                JwtRegisteredClaimNames.Iat),
            RequiredLongClaim(
                principal,
                JwtRegisteredClaimNames.Exp),
            principal);
    }

    private async Task<LiveAuthCostShieldRemoteResult>
        ConsumeRemotelyAsync(
            string token,
            LiveAuthCostShieldClaims claims,
            CancellationToken cancellationToken)
    {
        var options = _options.Value;
        if (string.IsNullOrWhiteSpace(options.SecretKey))
        {
            throw ConfigurationError(
                "missing_secret_key",
                "SecretKey is required to consume CostShield tokens.");
        }

        var endpoint = new Uri(
            options.ApiUrl,
            "/api/costshield/authorizations/consume");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            options.SecretKey.Trim());
        request.Content = JsonContent.Create(new
        {
            token,
            action = claims.Action,
            environment = claims.Environment,
            origin = claims.Origin
        });

        HttpResponseMessage response;
        try
        {
            var client = _httpClientFactory.CreateClient(
                LiveAuthCostShieldDefaults.HttpClientName);
            response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new LiveAuthCostShieldException(
                "network_error",
                "LiveAuth could not be reached for token consumption.",
                retryable: true,
                innerException: exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw await CreateApiExceptionAsync(
                    response,
                    cancellationToken);

            var result = await response.Content.ReadFromJsonAsync<
                RemoteAuthorizationResponse>(
                JsonOptions,
                cancellationToken);
            if (result == null ||
                !result.Verified ||
                !string.Equals(
                    result.Action,
                    claims.Action,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    result.Environment,
                    claims.Environment,
                    StringComparison.Ordinal))
            {
                throw AuthorizationError(
                    "invalid_verification_response",
                    "LiveAuth returned an invalid verification response.");
            }

            return new LiveAuthCostShieldRemoteResult(
                result.Verified,
                result.Consumed,
                result.AuthorizationId,
                result.Action,
                result.Environment,
                result.Origin,
                result.VerificationMethod,
                result.ExpiresAtUnix,
                result.RequireSingleUse);
        }
    }

    private async Task<LiveAuthCostShieldException>
        CreateApiExceptionAsync(
            HttpResponseMessage response,
            CancellationToken cancellationToken)
    {
        LiveAuthErrorResponse? body = null;
        try
        {
            body = await response.Content.ReadFromJsonAsync<
                LiveAuthErrorResponse>(
                JsonOptions,
                cancellationToken);
        }
        catch (JsonException)
        {
            // Keep the HTTP status when an upstream error is not JSON.
        }

        var code = string.IsNullOrWhiteSpace(body?.Error)
            ? $"http_{(int)response.StatusCode}"
            : body.Error;
        var message =
            body?.ErrorDescription ??
            body?.Message ??
            $"LiveAuth request failed with status {(int)response.StatusCode}.";
        return new LiveAuthCostShieldException(
            code,
            message,
            response.StatusCode,
            retryable:
                response.StatusCode is
                    HttpStatusCode.RequestTimeout or
                    HttpStatusCode.TooManyRequests ||
                (int)response.StatusCode >= 500);
    }

    private JwtSecurityToken ReadToken(string token)
    {
        try
        {
            return _handler.ReadJwtToken(token);
        }
        catch (Exception exception)
            when (exception is ArgumentException or
                SecurityTokenException)
        {
            throw AuthorizationError(
                "invalid_token",
                "The CostShield token is malformed.",
                exception);
        }
    }

    private static void ValidateHeader(JwtSecurityToken token)
    {
        if (!string.Equals(
                token.Header.Alg,
                SecurityAlgorithms.RsaSha256,
                StringComparison.Ordinal) ||
            !string.Equals(
                token.Header.Typ,
                LiveAuthCostShieldDefaults.TokenType,
                StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(token.Header.Kid))
        {
            throw AuthorizationError(
                "invalid_token_header",
                "The CostShield token header is invalid.");
        }
    }

    private static string RequiredClaim(
        ClaimsPrincipal principal,
        string claimType)
    {
        var value = principal.FindFirst(claimType)?.Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw AuthorizationError(
                "invalid_token_claims",
                $"The CostShield token is missing the {claimType} claim.");
        }
        return value;
    }

    private static int RequiredIntClaim(
        ClaimsPrincipal principal,
        string claimType)
    {
        if (!int.TryParse(
                RequiredClaim(principal, claimType),
                out var value))
        {
            throw AuthorizationError(
                "invalid_token_claims",
                $"The CostShield token has an invalid {claimType} claim.");
        }
        return value;
    }

    private static long RequiredLongClaim(
        ClaimsPrincipal principal,
        string claimType)
    {
        if (!long.TryParse(
                RequiredClaim(principal, claimType),
                out var value))
        {
            throw AuthorizationError(
                "invalid_token_claims",
                $"The CostShield token has an invalid {claimType} claim.");
        }
        return value;
    }

    private static bool RequiredBoolClaim(
        ClaimsPrincipal principal,
        string claimType)
    {
        if (!bool.TryParse(
                RequiredClaim(principal, claimType),
                out var value))
        {
            throw AuthorizationError(
                "invalid_token_claims",
                $"The CostShield token has an invalid {claimType} claim.");
        }
        return value;
    }

    private static void ValidateTokenInput(string token)
    {
        if (string.IsNullOrWhiteSpace(token) ||
            token.Length > LiveAuthCostShieldDefaults.MaximumTokenLength)
        {
            throw AuthorizationError(
                "invalid_token",
                "The CostShield token is missing or too large.");
        }
    }

    private static string ValidateAction(string action)
    {
        action = action?.Trim() ?? string.Empty;
        if (action.Length is 0 or > 100)
        {
            throw ConfigurationError(
                "invalid_action",
                "action is required and must be 100 characters or less.");
        }
        return action;
    }

    internal static string NormalizeOrigin(string origin)
    {
        if (!Uri.TryCreate(origin.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath != "/")
        {
            throw ConfigurationError(
                "invalid_origin",
                "origin must be an absolute HTTP or HTTPS origin.");
        }

        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }

    private static LiveAuthCostShieldException AuthorizationError(
        string code,
        string message,
        Exception? innerException = null,
        HttpStatusCode statusCode = HttpStatusCode.Unauthorized)
        => new(
            code,
            message,
            statusCode,
            innerException: innerException);

    private static LiveAuthCostShieldException ConfigurationError(
        string code,
        string message)
        => new(code, message);
}
