using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Models.CostShield;
using Microsoft.IdentityModel.Tokens;

namespace LiveAuthCore.Services.CostShield;

public interface ICostShieldTokenService
{
    string Issue(
        CostShieldAuthorization authorization,
        Project project,
        ProtectedAction action,
        string? subject);

    CostShieldTokenValidationResult Validate(string token);

    CostShieldJwksResponse GetJwks();
}

public sealed class CostShieldTokenService : ICostShieldTokenService, IDisposable
{
    public const string DefaultIssuer = "https://api.liveauth.app";
    public const string DefaultAudience = "liveauth-costshield";

    private readonly RSA _rsa;
    private readonly RsaSecurityKey _securityKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly JwtSecurityTokenHandler _handler = new()
    {
        MapInboundClaims = false,
        MaximumTokenSizeInBytes = 8 * 1024
    };

    public CostShieldTokenService(
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<CostShieldTokenService> logger)
    {
        _issuer = configuration["CostShield:TokenIssuer"] ?? DefaultIssuer;
        _audience = configuration["CostShield:TokenAudience"] ?? DefaultAudience;
        var keyId = configuration["CostShield:SigningKeyId"] ?? "costshield-rs256-v1";
        var privateKeyPem = configuration["CostShield:SigningPrivateKeyPem"];

        _rsa = RSA.Create();

        if (string.IsNullOrWhiteSpace(privateKeyPem))
        {
            if (environment.IsProduction())
            {
                throw new InvalidOperationException(
                    "CostShield:SigningPrivateKeyPem is required in production.");
            }

            _rsa.KeySize = 2048;
            logger.LogWarning(
                "CostShield is using an ephemeral RS256 key. Configure CostShield:SigningPrivateKeyPem before production.");
        }
        else
        {
            _rsa.ImportFromPem(privateKeyPem.Replace("\\n", "\n", StringComparison.Ordinal));
        }

        _securityKey = new RsaSecurityKey(_rsa)
        {
            KeyId = keyId
        };
    }

    public string Issue(
        CostShieldAuthorization authorization,
        Project project,
        ProtectedAction action,
        string? subject)
    {
        var issuedAt = new DateTimeOffset(authorization.IssuedAt).ToUnixTimeSeconds();
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Jti, authorization.TokenId),
            new(JwtRegisteredClaimNames.Iat, issuedAt.ToString(), ClaimValueTypes.Integer64),
            new("projectId", project.Id.ToString()),
            new("projectPublicKey", project.PublicKey),
            new("environment", authorization.Environment),
            new("action", action.Name),
            new("protectedActionId", action.Id.ToString()),
            new("verificationMethod", authorization.VerificationMethod),
            new("difficulty", authorization.Difficulty.ToString(), ClaimValueTypes.Integer32),
            new("clientContextHash", authorization.ClientContextHash),
            new("singleUse", authorization.RequireSingleUse ? "true" : "false", ClaimValueTypes.Boolean),
            new("configurationVersion", authorization.ConfigurationVersion.ToString(), ClaimValueTypes.Integer32)
        };

        if (!string.IsNullOrWhiteSpace(authorization.Origin))
            claims.Add(new Claim("origin", authorization.Origin));

        if (!string.IsNullOrWhiteSpace(subject))
        {
            claims.Add(new Claim("clientSubject", Truncate(subject.Trim(), 256)));
            claims.Add(new Claim("subjectSource", "client"));
        }

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            notBefore: authorization.IssuedAt.AddSeconds(-5),
            expires: authorization.ExpiresAt,
            signingCredentials: new SigningCredentials(_securityKey, SecurityAlgorithms.RsaSha256));

        token.Header["kid"] = _securityKey.KeyId;
        token.Header["typ"] = "costshield+jwt";
        return _handler.WriteToken(token);
    }

    public CostShieldTokenValidationResult Validate(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 8 * 1024)
            return CostShieldTokenValidationResult.Invalid("invalid_token");

        try
        {
            var principal = _handler.ValidateToken(
                token,
                new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = _securityKey,
                    ValidateIssuer = true,
                    ValidIssuer = _issuer,
                    ValidateAudience = true,
                    ValidAudience = _audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    RequireSignedTokens = true,
                    RequireExpirationTime = true,
                    ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 }
                },
                out var validatedToken);

            if (validatedToken is not JwtSecurityToken jwt ||
                !string.Equals(jwt.Header.Alg, SecurityAlgorithms.RsaSha256, StringComparison.Ordinal))
            {
                return CostShieldTokenValidationResult.Invalid("invalid_algorithm");
            }

            return CostShieldTokenValidationResult.Valid(principal);
        }
        catch (SecurityTokenExpiredException)
        {
            return CostShieldTokenValidationResult.Invalid("token_expired");
        }
        catch (SecurityTokenException)
        {
            return CostShieldTokenValidationResult.Invalid("invalid_token");
        }
        catch (ArgumentException)
        {
            return CostShieldTokenValidationResult.Invalid("invalid_token");
        }
    }

    public CostShieldJwksResponse GetJwks()
    {
        var parameters = _rsa.ExportParameters(includePrivateParameters: false);
        var key = new CostShieldJwk(
            Kty: "RSA",
            Use: "sig",
            Kid: _securityKey.KeyId,
            Alg: SecurityAlgorithms.RsaSha256,
            N: Base64UrlEncoder.Encode(parameters.Modulus),
            E: Base64UrlEncoder.Encode(parameters.Exponent));

        return new CostShieldJwksResponse(new[] { key });
    }

    public void Dispose()
    {
        _rsa.Dispose();
    }

    private static string Truncate(string value, int maximumLength)
        => value.Length <= maximumLength ? value : value[..maximumLength];
}

public sealed record CostShieldTokenValidationResult(
    bool IsValid,
    ClaimsPrincipal? Principal,
    string? Error)
{
    public static CostShieldTokenValidationResult Valid(ClaimsPrincipal principal)
        => new(true, principal, null);

    public static CostShieldTokenValidationResult Invalid(string error)
        => new(false, null, error);
}
