using System.Net.Http.Json;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using LiveAuthCore.Data.Entities;

namespace LiveAuthCore.Services.Meter;

public sealed record MeterInvoice(string PaymentHash, string Bolt11, long AmountSats, DateTime ExpiresAt);
public sealed record MeterInvoiceStatus(bool Settled, DateTime? SettledAt);
public sealed record MeterLightningConnectionStatus(bool Success, string? Alias, string? Version, string? Error);

public interface ILightningInvoiceProvider
{
    string ProviderType { get; }
    Task<MeterInvoice> CreateInvoiceAsync(MerchantLightningConnection connection, long amountSats, string memo, TimeSpan expiry, CancellationToken ct);
    Task<MeterInvoiceStatus> LookupInvoiceAsync(MerchantLightningConnection connection, string paymentHash, CancellationToken ct);
    Task<MeterLightningConnectionStatus> ValidateConnectionAsync(MerchantLightningConnection connection, CancellationToken ct);
}

public interface ILightningInvoiceProviderFactory
{
    ILightningInvoiceProvider Get(string providerType);
}

public sealed class LightningInvoiceProviderFactory : ILightningInvoiceProviderFactory
{
    private readonly IReadOnlyDictionary<string, ILightningInvoiceProvider> _providers;
    public LightningInvoiceProviderFactory(IEnumerable<ILightningInvoiceProvider> providers)
        => _providers = providers.ToDictionary(x => x.ProviderType, StringComparer.OrdinalIgnoreCase);

    public ILightningInvoiceProvider Get(string providerType)
        => _providers.TryGetValue(providerType, out var provider)
            ? provider
            : throw new InvalidOperationException($"Unsupported merchant Lightning provider '{providerType}'.");
}

public sealed class MerchantLndInvoiceProvider : ILightningInvoiceProvider
{
    private readonly IMeterSecretProtector _secrets;
    private readonly IMeterSsrfGuard _ssrf;
    private readonly IConfiguration _configuration;
    public string ProviderType => "LND_REST";

    public MerchantLndInvoiceProvider(IMeterSecretProtector secrets, IMeterSsrfGuard ssrf, IConfiguration configuration)
    {
        _secrets = secrets;
        _ssrf = ssrf;
        _configuration = configuration;
    }

    public async Task<MeterInvoice> CreateInvoiceAsync(
        MerchantLightningConnection connection,
        long amountSats,
        string memo,
        TimeSpan expiry,
        CancellationToken ct)
    {
        if (amountSats <= 0) throw new ArgumentOutOfRangeException(nameof(amountSats));
        using var client = await CreateClientAsync(connection, ct);
        using var request = CreateRequest(connection, HttpMethod.Post, "v1/invoices");
        request.Content = JsonContent.Create(new
        {
            memo = memo.Length <= 500 ? memo : memo[..500],
            value = amountSats.ToString(),
            expiry = Math.Max(60, (long)expiry.TotalSeconds).ToString(),
            @private = true
        });
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        var root = json.RootElement;
        var rHash = root.GetProperty("r_hash").GetString() ?? throw new InvalidOperationException("LND omitted r_hash.");
        var paymentHash = Convert.ToHexString(Convert.FromBase64String(rHash)).ToLowerInvariant();
        var bolt11 = root.GetProperty("payment_request").GetString() ?? throw new InvalidOperationException("LND omitted payment_request.");
        return new MeterInvoice(paymentHash, bolt11, amountSats, DateTime.UtcNow.Add(expiry));
    }

    public async Task<MeterInvoiceStatus> LookupInvoiceAsync(MerchantLightningConnection connection, string paymentHash, CancellationToken ct)
    {
        if (paymentHash.Length != 64 || !paymentHash.All(Uri.IsHexDigit))
            return new MeterInvoiceStatus(false, null);
        using var client = await CreateClientAsync(connection, ct);
        using var request = CreateRequest(connection, HttpMethod.Get, $"v1/invoice/{paymentHash.ToLowerInvariant()}");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) return new MeterInvoiceStatus(false, null);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        var root = json.RootElement;
        var settled = root.TryGetProperty("settled", out var settledElement) && settledElement.GetBoolean();
        if (root.TryGetProperty("state", out var state))
            settled |= string.Equals(state.GetString(), "SETTLED", StringComparison.OrdinalIgnoreCase);
        DateTime? settledAt = null;
        if (settled && root.TryGetProperty("settle_date", out var settleDate) &&
            long.TryParse(settleDate.GetString(), out var unix) && unix > 0)
            settledAt = DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;
        return new MeterInvoiceStatus(settled, settledAt);
    }

    public async Task<MeterLightningConnectionStatus> ValidateConnectionAsync(MerchantLightningConnection connection, CancellationToken ct)
    {
        try
        {
            using var client = await CreateClientAsync(connection, ct);
            using var request = CreateRequest(connection, HttpMethod.Get, "v1/getinfo");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                return new(false, null, null, $"LND returned HTTP {(int)response.StatusCode}.");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
            var root = json.RootElement;
            return new(true,
                root.TryGetProperty("alias", out var alias) ? alias.GetString() : null,
                root.TryGetProperty("version", out var version) ? version.GetString() : null,
                null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or MeterSecurityException or CryptographicException)
        {
            return new(false, null, null, "Unable to validate the merchant LND connection.");
        }
    }

    private HttpRequestMessage CreateRequest(MerchantLightningConnection connection, HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("Grpc-Metadata-macaroon", _secrets.Unprotect(connection.EncryptedMacaroon));
        return request;
    }

    private async Task<HttpClient> CreateClientAsync(MerchantLightningConnection connection, CancellationToken ct)
    {
        var allowInsecure = _configuration.GetValue("Meter:AllowInsecureLightningInTest", false);
        var allowPrivate = _configuration.GetValue("Meter:AllowPrivateLightningProviders", false);
        var resolved = await _ssrf.ValidateAndResolveAsync(connection.RestUrl, !allowInsecure, allowPrivate, ct);
        X509Certificate2? pinnedCertificate = null;
        if (!string.IsNullOrWhiteSpace(connection.EncryptedTlsCertificate))
        {
            var pem = _secrets.Unprotect(connection.EncryptedTlsCertificate);
            pinnedCertificate = X509Certificate2.CreateFromPem(pem);
        }

        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ConnectCallback = async (context, token) =>
            {
                Exception? last = null;
                foreach (var address in resolved.Addresses)
                {
                    var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        await socket.ConnectAsync(new System.Net.IPEndPoint(address, context.DnsEndPoint.Port), token);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (Exception ex) when (ex is SocketException or OperationCanceledException)
                    {
                        socket.Dispose();
                        last = ex;
                    }
                }
                throw new HttpRequestException("Unable to connect to validated LND address.", last);
            }
        };
        if (pinnedCertificate != null)
        {
            var expected = SHA256.HashData(pinnedCertificate.RawData);
            handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                certificate != null && CryptographicOperations.FixedTimeEquals(
                    expected, SHA256.HashData(new X509Certificate2(certificate).RawData));
        }

        var client = new HttpClient(handler) { BaseAddress = EnsureTrailingSlash(resolved.Uri), Timeout = TimeSpan.FromSeconds(15) };
        return client;
    }

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        var value = uri.AbsoluteUri.EndsWith('/') ? uri.AbsoluteUri : uri.AbsoluteUri + "/";
        return new Uri(value);
    }
}
