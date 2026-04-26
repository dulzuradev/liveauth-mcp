using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace LiveAuthCore.Services;

/// <summary>
/// Fetches BTC/USD exchange rate from CoinGecko (primary) or Binance (fallback).
/// Caches for 5 minutes to avoid hammering the API.
/// </summary>
public class BtcExchangeRateService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<BtcExchangeRateService> _logger;

    private const string CacheKey = "btc_usd_rate";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public BtcExchangeRateService(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        ILogger<BtcExchangeRateService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Returns BTC price in USD, or null if all sources fail.
    /// Tries CoinGecko first, then Binance as fallback.
    /// </summary>
    public async Task<double?> GetBtcUsdRateAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue<double>(CacheKey, out var cached))
            return cached;

        // Try CoinGecko first
        var rate = await TryCoinGeckoAsync(ct);
        if (rate.HasValue)
        {
            _cache.Set(CacheKey, rate.Value, CacheDuration);
            return rate;
        }

        // Fallback to Coinbase
        rate = await TryCoinbaseAsync(ct);
        if (rate.HasValue)
        {
            _cache.Set(CacheKey, rate.Value, CacheDuration);
            return rate;
        }

        _logger.LogWarning("All BTC/USD sources failed");
        return null;
    }

    private async Task<double?> TryCoinGeckoAsync(CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("coingecko");
            var response = await client.GetAsync(
                "https://api.coingecko.com/api/v3/simple/price?ids=bitcoin&vs_currencies=usd",
                ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("CoinGecko returned {StatusCode}", response.StatusCode);
                return null;
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (doc.RootElement.TryGetProperty("bitcoin", out var btc) &&
                btc.TryGetProperty("usd", out var usd))
            {
                return usd.GetDouble();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CoinGecko fetch failed");
        }
        return null;
    }

    private async Task<double?> TryCoinbaseAsync(CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("coingecko");
            var response = await client.GetAsync(
                "https://api.coinbase.com/v2/prices/spot?currency=USD",
                ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Coinbase returned {StatusCode}", response.StatusCode);
                return null;
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("amount", out var amount))
            {
                return double.Parse(amount.GetString()!);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Coinbase fetch failed");
        }
        return null;
    }

    /// <summary>
    /// Converts sats to USD. Returns null if exchange rate unavailable.
    /// </summary>
    public async Task<double?> SatsToUsdAsync(long sats, CancellationToken ct = default)
    {
        var rate = await GetBtcUsdRateAsync(ct);
        if (rate == null) return null;
        // sats / 100_000_000 = BTC, then * USD rate
        return sats / 100_000_000.0 * rate.Value;
    }
}
