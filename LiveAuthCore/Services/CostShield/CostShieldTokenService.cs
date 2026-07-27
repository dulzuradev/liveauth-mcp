using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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
    public const string DefaultKeyId = "costshield-rs256-v1";
    private const int MinimumRsaKeySize = 2048;

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
        var configuredKeyId = configuration["CostShield:SigningKeyId"];
        var keyId = configuredKeyId == null
            ? DefaultKeyId
            : configuredKeyId.Trim();
        if (!IsValidKeyId(keyId))
        {
            throw new InvalidOperationException(
                "CostShield:SigningKeyId must be 1-128 letters, numbers, dots, underscores, or hyphens.");
        }

        var privateKeyPem = ResolvePrivateKeyPem(configuration);

        _rsa = RSA.Create();

        if (string.IsNullOrWhiteSpace(privateKeyPem))
        {
            if (environment.IsProduction())
            {
                throw new InvalidOperationException(
                    "Configure CostShield:SigningPrivateKeyPem or " +
                    "CostShield:SigningPrivateKeyPemBase64 in production.");
            }

            _rsa.KeySize = 2048;
            logger.LogWarning(
                "CostShield is using an ephemeral RS256 key. Configure CostShield:SigningPrivateKeyPem before production.");
        }
        else
        {
            try
            {
                _rsa.ImportFromPem(
                    privateKeyPem.Replace("\\n", "\n", StringComparison.Ordinal));
                EnsureProductionKeyIsSafe(_rsa);
            }
            catch
            {
                _rsa.Dispose();
                throw;
            }
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

    private static string? ResolvePrivateKeyPem(IConfiguration configuration)
    {
        var pem = configuration["CostShield:SigningPrivateKeyPem"];
        var pemBase64 = configuration["CostShield:SigningPrivateKeyPemBase64"];

        if (!string.IsNullOrWhiteSpace(pem) &&
            !string.IsNullOrWhiteSpace(pemBase64))
        {
            throw new InvalidOperationException(
                "Configure only one of CostShield:SigningPrivateKeyPem or CostShield:SigningPrivateKeyPemBase64.");
        }

        if (string.IsNullOrWhiteSpace(pemBase64))
            return pem;

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(
                pemBase64.Trim()));
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "CostShield:SigningPrivateKeyPemBase64 is not valid base64.",
                exception);
        }
    }

    private static void EnsureProductionKeyIsSafe(RSA rsa)
    {
        if (rsa.KeySize < MinimumRsaKeySize)
        {
            throw new InvalidOperationException(
                $"The CostShield RSA signing key must be at least {MinimumRsaKeySize} bits.");
        }

        try
        {
            if (rsa.ExportParameters(includePrivateParameters: true).D == null)
            {
                throw new InvalidOperationException(
                    "The CostShield signing key must contain private key material.");
            }
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException(
                "The CostShield signing key must contain exportable private key material.",
                exception);
        }
    }

    private static bool IsValidKeyId(string keyId)
        => keyId.Length is > 0 and <= 128 &&
           keyId.All(character =>
               char.IsAsciiLetterOrDigit(character) ||
               character is '.' or '_' or '-');
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
