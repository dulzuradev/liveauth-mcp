using System.Security.Cryptography;
using System.Text;

namespace LiveAuthCore.Services;

public sealed class PowChallengeSigner
{
    private readonly byte[] _key;

    public PowChallengeSigner(IConfiguration config)
    {
        var secret = config["LiveAuth:PowHmacSecret"];
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Missing config LiveAuth:PowHmacSecret");

        _key = Encoding.UTF8.GetBytes(secret);
    }

    public string Sign(string payload)
    {
        using var hmac = new HMACSHA256(_key);
        var sigBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(sigBytes).ToLowerInvariant();
    }

    public bool Verify(string payload, string sigHex)
    {
        var expected = Sign(payload);

        // constant-time compare
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(sigHex.ToLowerInvariant())
        );
    }
}