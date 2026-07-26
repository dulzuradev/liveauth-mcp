using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LiveAuth.CostShield.AspNetCore;

internal interface ICostShieldJwksProvider
{
    Task<IReadOnlyList<SecurityKey>> GetSigningKeysAsync(
        bool forceRefresh,
        CancellationToken cancellationToken);
}

internal sealed class CostShieldJwksProvider : ICostShieldJwksProvider
{
    private const int MaximumJwksBytes = 256 * 1024;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<LiveAuthCostShieldOptions> _options;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private IReadOnlyList<SecurityKey>? _keys;
    private DateTimeOffset _expiresAt;

    public CostShieldJwksProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<LiveAuthCostShieldOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public async Task<IReadOnlyList<SecurityKey>> GetSigningKeysAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (!forceRefresh &&
            _keys is { Count: > 0 } &&
            _expiresAt > DateTimeOffset.UtcNow)
        {
            return _keys;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh &&
                _keys is { Count: > 0 } &&
                _expiresAt > DateTimeOffset.UtcNow)
            {
                return _keys;
            }

            var client = _httpClientFactory.CreateClient(
                LiveAuthCostShieldDefaults.HttpClientName);
            var endpoint = new Uri(
                _options.Value.ApiUrl,
                "/api/public/costshield/.well-known/jwks.json");

            using var response = await client.GetAsync(
                endpoint,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new LiveAuthCostShieldException(
                    "jwks_unavailable",
                    "LiveAuth signing keys could not be loaded.",
                    response.StatusCode,
                    retryable: (int)response.StatusCode >= 500);
            }

            if (response.Content.Headers.ContentLength > MaximumJwksBytes)
            {
                throw new LiveAuthCostShieldException(
                    "invalid_jwks",
                    "LiveAuth returned an oversized signing-key response.");
            }

            var json = await response.Content.ReadAsStringAsync(
                cancellationToken);
            if (json.Length > MaximumJwksBytes)
            {
                throw new LiveAuthCostShieldException(
                    "invalid_jwks",
                    "LiveAuth returned an oversized signing-key response.");
            }

            JsonWebKeySet keySet;
            try
            {
                keySet = new JsonWebKeySet(json);
            }
            catch (Exception exception)
                when (exception is ArgumentException or JsonException)
            {
                throw new LiveAuthCostShieldException(
                    "invalid_jwks",
                    "LiveAuth returned invalid signing keys.",
                    innerException: exception);
            }

            var keys = keySet.Keys
                .Where(key =>
                    string.Equals(key.Kty, "RSA", StringComparison.Ordinal) &&
                    string.Equals(key.Use, "sig", StringComparison.Ordinal) &&
                    string.Equals(key.Alg, "RS256", StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(key.Kid))
                .Cast<SecurityKey>()
                .ToList();
            if (keys.Count == 0)
            {
                throw new LiveAuthCostShieldException(
                    "invalid_jwks",
                    "LiveAuth returned no usable signing keys.");
            }

            _keys = keys;
            _expiresAt = DateTimeOffset.UtcNow +
                _options.Value.JwksCacheDuration;
            return _keys;
        }
        catch (LiveAuthCostShieldException)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            throw new LiveAuthCostShieldException(
                "jwks_unavailable",
                "LiveAuth signing keys could not be loaded.",
                retryable: true,
                innerException: exception);
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}
