using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LiveAuthCore.Data.Entities;

namespace LiveAuthCore.Services.Meter;

public sealed record MeterCredentialPayload(
    int Version,
    Guid ChallengeId,
    Guid ProjectId,
    string Environment,
    Guid? RouteRuleId,
    string Method,
    string PathPattern,
    long PriceSats,
    string PaymentHash,
    long ExpiresAtUnix,
    int MaximumUses,
    string Nonce,
    string? RequestBodyHash);

public sealed record MeterL402Authorization(string Macaroon, string Preimage);

public interface IMeterCredentialService
{
    string Issue(MeterPaymentChallenge challenge);
    bool TryValidate(string token, out MeterCredentialPayload? payload, out string error);
    bool TryParseAuthorization(string? header, out MeterL402Authorization? authorization);
    bool PreimageMatches(string preimage, string paymentHash);
}

public sealed class MeterCredentialService : IMeterCredentialService
{
    private readonly byte[] _key;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public MeterCredentialService(IConfiguration configuration)
    {
        var secret = configuration["Meter:CredentialSigningKey"] ??
            configuration["Jwt:SigningKey"] ?? configuration["Jwt:Key"] ??
            configuration["LiveAuth:PowHmacSecret"];
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Meter credential signing key is not configured.");
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }

    public string Issue(MeterPaymentChallenge challenge)
    {
        var payload = new MeterCredentialPayload(
            1, challenge.Id, challenge.ProjectId, challenge.Environment,
            challenge.RouteRuleId, challenge.HttpMethod, challenge.NormalizedRoute,
            challenge.PriceSats, challenge.PaymentHash, new DateTimeOffset(challenge.CredentialExpiresAt).ToUnixTimeSeconds(),
            challenge.MaximumUses, challenge.CredentialNonce, challenge.RequestBodyHash);
        var encoded = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions));
        return $"{encoded}.{Base64Url(Sign(encoded))}";
    }

    public bool TryValidate(string token, out MeterCredentialPayload? payload, out string error)
    {
        payload = null;
        error = "invalid_credential";
        if (string.IsNullOrWhiteSpace(token) || token.Length > 8192) return false;
        var parts = token.Split('.');
        if (parts.Length != 2) return false;
        byte[] supplied;
        try { supplied = FromBase64Url(parts[1]); }
        catch (FormatException) { return false; }
        var expected = Sign(parts[0]);
        if (supplied.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(supplied, expected)) return false;
        try { payload = JsonSerializer.Deserialize<MeterCredentialPayload>(FromBase64Url(parts[0]), JsonOptions); }
        catch (JsonException) { return false; }
        if (payload == null || payload.Version != 1) return false;
        if (payload.ExpiresAtUnix <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            error = "credential_expired";
            return false;
        }
        error = string.Empty;
        return true;
    }

    public bool TryParseAuthorization(string? header, out MeterL402Authorization? authorization)
    {
        authorization = null;
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("L402 ", StringComparison.OrdinalIgnoreCase)) return false;
        var value = header[5..].Trim();
        var separator = value.LastIndexOf(':');
        if (separator <= 0 || separator == value.Length - 1) return false;
        authorization = new(value[..separator], value[(separator + 1)..]);
        return authorization.Macaroon.Length <= 8192 && authorization.Preimage.Length <= 256;
    }

    public bool PreimageMatches(string preimage, string paymentHash)
    {
        byte[] preimageBytes;
        try
        {
            preimageBytes = preimage.Length == 64 && preimage.All(Uri.IsHexDigit)
                ? Convert.FromHexString(preimage)
                : Convert.FromBase64String(preimage);
        }
        catch (FormatException) { return false; }
        if (preimageBytes.Length != 32 || paymentHash.Length != 64 || !paymentHash.All(Uri.IsHexDigit)) return false;
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(preimageBytes), Convert.FromHexString(paymentHash));
    }

    private byte[] Sign(string payload)
    {
        using var hmac = new HMACSHA256(_key);
        return hmac.ComputeHash(Encoding.ASCII.GetBytes(payload));
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '='));
    }
}
