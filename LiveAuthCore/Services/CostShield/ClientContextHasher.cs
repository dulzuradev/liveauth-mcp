using System.Security.Cryptography;
using System.Text;

namespace LiveAuthCore.Services.CostShield;

public interface IClientContextHasher
{
    string HashIp(string? ipAddress);
    string HashSubject(string? subject);
    string HashContext(Guid projectId, string? ipAddress, string? userAgent, string? subject);
}

public sealed class ClientContextHasher : IClientContextHasher
{
    private readonly byte[] _key;

    public ClientContextHasher(IConfiguration configuration)
    {
        var secret =
            configuration["CostShield:ContextHmacSecret"] ??
            configuration["LiveAuth:PowHmacSecret"];

        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException(
                "CostShield context hashing requires CostShield:ContextHmacSecret or LiveAuth:PowHmacSecret.");

        _key = Encoding.UTF8.GetBytes(secret);
    }

    public string HashIp(string? ipAddress)
        => Hash($"ip:{Normalize(ipAddress, 128)}");

    public string HashSubject(string? subject)
        => Hash($"subject:{Normalize(subject, 256)}");

    public string HashContext(
        Guid projectId,
        string? ipAddress,
        string? userAgent,
        string? subject)
    {
        var payload = string.Join(
            '\n',
            "costshield-context-v1",
            projectId.ToString("N"),
            Normalize(ipAddress, 128),
            Normalize(userAgent, 512),
            Normalize(subject, 256));

        return Hash(payload);
    }

    private string Hash(string value)
    {
        using var hmac = new HMACSHA256(_key);
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
    }

    private static string Normalize(string? value, int maximumLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength];
    }
}
