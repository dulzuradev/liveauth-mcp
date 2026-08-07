using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace LiveAuthCore.Services.Meter;

public interface IMeterSecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedValue);
}

public sealed class MeterSecretProtector : IMeterSecretProtector
{
    private readonly byte[] _key;

    public MeterSecretProtector(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configured = configuration["Meter:EncryptionKey"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            if (environment.IsProduction())
                throw new InvalidOperationException("Meter:EncryptionKey is required before storing merchant credentials in production.");
            configured = configuration["LiveAuth:PowHmacSecret"]
                ?? throw new InvalidOperationException("Meter secret protection requires Meter:EncryptionKey.");
        }

        _key = DecodeKey(configured);
    }

    public string Protect(string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var input = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[input.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key, tag.Length);
        aes.Encrypt(nonce, input, ciphertext, tag, Encoding.UTF8.GetBytes("liveauth-meter-secret-v1"));
        return $"v1.{Base64Url(nonce)}.{Base64Url(ciphertext)}.{Base64Url(tag)}";
    }

    public string Unprotect(string protectedValue)
    {
        var parts = protectedValue.Split('.');
        if (parts.Length != 4 || parts[0] != "v1")
            throw new CryptographicException("Unsupported encrypted Meter secret.");
        var nonce = FromBase64Url(parts[1]);
        var ciphertext = FromBase64Url(parts[2]);
        var tag = FromBase64Url(parts[3]);
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(_key, tag.Length);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, Encoding.UTF8.GetBytes("liveauth-meter-secret-v1"));
        return Encoding.UTF8.GetString(plaintext);
    }

    private static byte[] DecodeKey(string configured)
    {
        try
        {
            var raw = Convert.FromBase64String(configured.Trim());
            if (raw.Length == 32) return raw;
        }
        catch (FormatException) { }

        return SHA256.HashData(Encoding.UTF8.GetBytes(configured));
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value)
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Convert.FromBase64String(padded);
    }
}

public interface IMeterSsrfGuard
{
    Task<MeterResolvedDestination> ValidateAndResolveAsync(
        string url,
        bool requireHttps,
        bool allowPrivate,
        CancellationToken ct);
}

public sealed record MeterResolvedDestination(Uri Uri, IReadOnlyList<IPAddress> Addresses);

public sealed class MeterSsrfGuard : IMeterSsrfGuard
{
    public async Task<MeterResolvedDestination> ValidateAndResolveAsync(
        string url,
        bool requireHttps,
        bool allowPrivate,
        CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            !string.IsNullOrEmpty(uri.UserInfo) || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new MeterSecurityException("invalid_destination", "Destination must be an absolute HTTP(S) URL without user information.");
        }

        if (requireHttps && uri.Scheme != Uri.UriSchemeHttps)
            throw new MeterSecurityException("https_required", "HTTPS is required for this destination.");

        var addresses = IPAddress.TryParse(uri.Host, out var literal)
            ? new[] { literal }
            : await Dns.GetHostAddressesAsync(uri.DnsSafeHost, ct);

        if (addresses.Length == 0)
            throw new MeterSecurityException("dns_resolution_failed", "Destination did not resolve.");

        if (!allowPrivate && addresses.Any(IsProhibited))
            throw new MeterSecurityException("destination_blocked", "Destination resolves to a private or reserved address.");

        // Reject mixed public/private answers too. The proxy pins one of these validated
        // addresses at connection time, preventing a second DNS lookup/rebinding.
        if (addresses.Any(IsMetadata))
            throw new MeterSecurityException("destination_blocked", "Metadata-service destinations are not allowed.");

        return new MeterResolvedDestination(uri, addresses);
    }

    public static bool IsProhibited(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.None)) return true;

        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] == 10 || bytes[0] == 127 || bytes[0] == 0 ||
                   (bytes[0] == 169 && bytes[1] == 254) ||
                   (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168) ||
                   (bytes[0] == 100 && bytes[1] is >= 64 and <= 127) ||
                   bytes[0] >= 224;
        }

        return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast ||
               (bytes.Length == 16 && (bytes[0] & 0xfe) == 0xfc);
    }

    private static bool IsMetadata(IPAddress address)
        => address.MapToIPv4().Equals(IPAddress.Parse("169.254.169.254"));
}

public sealed class MeterSecurityException : Exception
{
    public MeterSecurityException(string code, string message) : base(message) => Code = code;
    public string Code { get; }
}
