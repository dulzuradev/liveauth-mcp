using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using LiveAuthCore.Data.Entities;

namespace LiveAuthCore.Services.Meter;

public sealed record MeterProxyResult(int StatusCode, long OriginLatencyMilliseconds, long GatewayLatencyMilliseconds);

public interface IMeterOriginProxy
{
    Task<MeterProxyResult> ForwardAsync(HttpContext context, MeterProjectSettings settings,
        string path, byte[] body, Stopwatch gatewayClock,
        Func<int, long, long, Task<IReadOnlyDictionary<string, string>>> beforeHeaders,
        CancellationToken ct);
}

public sealed class MeterOriginProxy : IMeterOriginProxy
{
    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization", "TE",
        "Trailer", "Transfer-Encoding", "Upgrade"
    };
    private readonly IMeterSsrfGuard _ssrf;

    public MeterOriginProxy(IMeterSsrfGuard ssrf) => _ssrf = ssrf;

    public async Task<MeterProxyResult> ForwardAsync(HttpContext context, MeterProjectSettings settings,
        string path, byte[] body, Stopwatch gatewayClock,
        Func<int, long, long, Task<IReadOnlyDictionary<string, string>>> beforeHeaders,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.OriginBaseUrl))
            throw new MeterConfigurationException("origin_not_configured", "Meter origin is not configured.");
        var requireHttps = settings.Environment == MeterEnvironments.Live;
        var allowPrivate = settings.Environment == MeterEnvironments.Test && settings.AllowPrivateOriginInTest;
        var resolved = await _ssrf.ValidateAndResolveAsync(settings.OriginBaseUrl, requireHttps, allowPrivate, ct);
        var destination = BuildDestination(resolved.Uri, path, context.Request.QueryString.Value);
        using var handler = CreatePinnedHandler(resolved.Addresses);
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), destination);
        CopyRequestHeaders(context, request);
        if (body.Length > 0 || HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPut(context.Request.Method) ||
            HttpMethods.IsPatch(context.Request.Method))
        {
            request.Content = new ByteArrayContent(body);
            foreach (var header in context.Request.Headers)
            {
                if (header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                    request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.OriginTimeoutSeconds, 1, 120)));
        var originClock = Stopwatch.StartNew();
        HttpResponseMessage origin;
        try
        {
            origin = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            throw new MeterProxyException("origin_timeout", StatusCodes.Status504GatewayTimeout, "The origin did not respond in time.");
        }
        catch (HttpRequestException)
        {
            throw new MeterProxyException("origin_unavailable", StatusCodes.Status502BadGateway, "The origin could not be reached.");
        }
        using (origin)
        {
            originClock.Stop();
            if (origin.Content.Headers.ContentLength > settings.MaximumResponseBodyBytes)
                throw new MeterProxyException("origin_response_too_large", StatusCodes.Status502BadGateway, "The origin response exceeds the configured limit.");

            var statusCode = (int)origin.StatusCode;
            var gatewayLatency = gatewayClock.ElapsedMilliseconds;
            var metadata = await beforeHeaders(statusCode, originClock.ElapsedMilliseconds, gatewayLatency);
            context.Response.StatusCode = statusCode;
            CopyResponseHeaders(origin, context.Response);
            foreach (var pair in metadata) context.Response.Headers[pair.Key] = pair.Value;
            context.Response.Headers["X-LiveAuth-Origin-Latency-Ms"] = originClock.ElapsedMilliseconds.ToString();

            await using var stream = await origin.Content.ReadAsStreamAsync(timeout.Token);
            var buffer = new byte[64 * 1024];
            long written = 0;
            while (true)
            {
                var read = await stream.ReadAsync(buffer, timeout.Token);
                if (read == 0) break;
                written += read;
                if (written > settings.MaximumResponseBodyBytes)
                {
                    context.Abort();
                    throw new MeterProxyException("origin_response_too_large", StatusCodes.Status502BadGateway,
                        "The origin response exceeded the configured streaming limit.");
                }
                await context.Response.Body.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                await context.Response.Body.FlushAsync(timeout.Token);
            }
            return new(statusCode, originClock.ElapsedMilliseconds, gatewayClock.ElapsedMilliseconds);
        }
    }

    private static SocketsHttpHandler CreatePinnedHandler(IReadOnlyList<IPAddress> addresses)
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ConnectCallback = async (connection, ct) =>
            {
                Exception? last = null;
                foreach (var address in addresses)
                {
                    var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(address, connection.DnsEndPoint.Port), ct);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (Exception ex) when (ex is SocketException or OperationCanceledException)
                    {
                        socket.Dispose();
                        last = ex;
                    }
                }
                throw new HttpRequestException("Unable to connect to validated origin address.", last);
            }
        };
    }

    private static Uri BuildDestination(Uri origin, string path, string? query)
    {
        var builder = new UriBuilder(origin);
        var basePath = builder.Path.TrimEnd('/');
        builder.Path = basePath + (path.StartsWith('/') ? path : "/" + path);
        builder.Query = query?.TrimStart('?') ?? string.Empty;
        return builder.Uri;
    }

    private static void CopyRequestHeaders(HttpContext context, HttpRequestMessage request)
    {
        foreach (var header in context.Request.Headers)
        {
            if (HopByHopHeaders.Contains(header.Key) || header.Key.StartsWith("X-LiveAuth-", StringComparison.OrdinalIgnoreCase) ||
                header.Key.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase) &&
                 header.Value.ToString().StartsWith("L402 ", StringComparison.OrdinalIgnoreCase)) ||
                header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase)) continue;
            request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", context.Request.Scheme);
        request.Headers.TryAddWithoutValidation("X-Forwarded-Host", context.Request.Host.Value);
        request.Headers.TryAddWithoutValidation("X-LiveAuth-Meter", "1");
    }

    private static void CopyResponseHeaders(HttpResponseMessage origin, HttpResponse response)
    {
        foreach (var header in origin.Headers)
            if (!HopByHopHeaders.Contains(header.Key)) response.Headers[header.Key] = header.Value.ToArray();
        foreach (var header in origin.Content.Headers)
            if (!HopByHopHeaders.Contains(header.Key) && !string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                response.Headers[header.Key] = header.Value.ToArray();
        response.Headers.Remove("transfer-encoding");
    }
}

public sealed class MeterProxyException : Exception
{
    public MeterProxyException(string code, int statusCode, string message) : base(message)
    { Code = code; StatusCode = statusCode; }
    public string Code { get; }
    public int StatusCode { get; }
}
