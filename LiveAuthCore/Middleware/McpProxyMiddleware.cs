using System.Text;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Middleware;

/// <summary>
/// L402-Gated MCP Proxy Middleware
/// 
/// Validates L402 tokens and forwards requests to registered MCP servers.
/// 
/// Usage: Register MCP proxy via /api/mcpproxy, then access via /mcp/{path}
/// </summary>
public class McpProxyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly LiveAuthDbContext _db;
    private readonly L402Service _l402;
    private readonly ILogger<McpProxyMiddleware> _logger;
    private static readonly HttpClient _httpClient = new();

    public McpProxyMiddleware(
        RequestDelegate next,
        LiveAuthDbContext db,
        L402Service l402,
        ILogger<McpProxyMiddleware> logger)
    {
        _next = next;
        _db = db;
        _l402 = l402;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        
        // Only handle /mcp/* paths
        if (!path.StartsWith("/mcp/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var proxyPath = path[5..].TrimStart('/');
        
        // Find the proxy by custom path or ID prefix
        var proxy = await FindProxyAsync(proxyPath);
        if (proxy == null)
        {
            _logger.LogWarning("MCP proxy not found for path: {Path}", proxyPath);
            context.Response.StatusCode = 404;
            await context.Response.WriteAsJsonAsync(new { error = "Proxy not found" });
            return;
        }

        if (!proxy.IsActive)
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new { error = "Proxy is disabled" });
            return;
        }

        // Check for L402 token in Authorization header
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        string? l402Token = null;

        if (authHeader?.StartsWith("L402 ") == true)
        {
            l402Token = authHeader[5..].Trim();
        }

        // Also check query parameter for convenience
        if (string.IsNullOrEmpty(l402Token))
        {
            l402Token = context.Request.Query["token"].FirstOrDefault();
        }

        if (string.IsNullOrEmpty(l402Token))
        {
            // No token - require payment
            await RequirePayment(context, proxy);
            return;
        }

        // Validate the L402 token
        var tokenHash = await _l402.ValidateTokenAsync(l402Token ?? "");
        if (string.IsNullOrEmpty(tokenHash))
        {
            // Token invalid or expired - require payment
            await RequirePayment(context, proxy);
            return;
        }

        // Token valid - forward request to upstream MCP server
        await ForwardRequest(context, proxy);
    }

    private async Task<McpProxy?> FindProxyAsync(string pathOrId)
    {
        // Try custom path first
        var proxy = await _db.McpProxies
            .FirstOrDefaultAsync(p => p.CustomPath == pathOrId && p.IsActive);
        
        if (proxy != null)
            return proxy;

        // Try by ID prefix
        if (Guid.TryParseExact(pathOrId[..Math.Min(8, pathOrId.Length)], "N", out _))
        {
            // Search by ID prefix
            var allProxies = await _db.McpProxies
                .Where(p => p.IsActive)
                .ToListAsync();
            
            return allProxies.FirstOrDefault(p => 
                p.Id.ToString("N").StartsWith(pathOrId, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private async Task RequirePayment(HttpContext context, McpProxy proxy)
    {
        // Generate invoice
        var invoice = await _l402.CreateInvoiceAsync(
            $"MCP proxy: {proxy.Name}",
            proxy.SatsPerRequest);

        // Update stats (we'll track attempted requests)
        proxy.TotalRequests++;
        await _db.SaveChangesAsync();

        // Return 402 Payment Required with invoice
        context.Response.StatusCode = 402;
        context.Response.Headers["WWW-Authenticate"] = $"L402 token=\"{invoice.Token}\", expiry={invoice.ExpiresAtUnix}";
        
        await context.Response.WriteAsJsonAsync(new
        {
            error = "Payment required",
            mode = "l402",
            expiresAt = invoice.ExpiresAtUnix,
            invoice = invoice.Bolt11,
            amountSats = proxy.SatsPerRequest,
            proxy = proxy.Name,
            instructions = "Pay the invoice and include the preimage as: Authorization: L402 <preimage>"
        });
    }

    private async Task ForwardRequest(HttpContext context, McpProxy proxy)
    {
        var upstreamUrl = proxy.UpstreamUrl;
        var path = context.Request.Path.Value?.Replace("/mcp/", "", StringComparison.OrdinalIgnoreCase) ?? "";
        
        // Strip the proxy path part
        if (!string.IsNullOrEmpty(proxy.CustomPath) && path.StartsWith(proxy.CustomPath, StringComparison.OrdinalIgnoreCase))
        {
            path = path[proxy.CustomPath.Length..].TrimStart('/');
        }
        else if (path.Length >= 8)
        {
            // Strip the ID prefix
            path = path[8..].TrimStart('/');
        }

        var targetUrl = $"{upstreamUrl}/{path}";
        if (context.Request.QueryString.HasValue)
        {
            targetUrl += context.Request.QueryString.Value;
        }

        try
        {
            var method = new HttpMethod(context.Request.Method);
            var request = new HttpRequestMessage(method, targetUrl);
            
            // Copy headers (except Host)
            foreach (var header in context.Request.Headers)
            {
                if (!header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) &&
                    !header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers.TryAddWithoutValidation(header.Key, (IEnumerable<string>)header.Value);
                }
            }

            // Copy body
            if (context.Request.ContentLength > 0)
            {
                request.Content = new StreamContent(context.Request.Body);
                if (!string.IsNullOrEmpty(context.Request.ContentType))
                    request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(context.Request.ContentType);
            }

            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            
            context.Response.StatusCode = (int)response.StatusCode;
            
            foreach (var header in response.Headers)
            {
                if (!header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.Headers[header.Key] = new Microsoft.Extensions.Primitives.StringValues(header.Value.ToArray());
                }
            }
            
            if (response.Content.Headers.ContentType != null)
            {
                context.Response.ContentType = response.Content.Headers.ContentType.ToString();
            }

            // Update stats on successful request
            proxy.TotalRequests++;
            proxy.TotalSatsEarned += proxy.SatsPerRequest;
            await _db.SaveChangesAsync();

            await response.Content.CopyToAsync(context.Response.Body);
            
            _logger.LogInformation("MCP proxy request forwarded: {Proxy} -> {Url}", proxy.Name, targetUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP proxy forward failed: {Proxy}", proxy.Name);
            context.Response.StatusCode = 502;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Upstream server error",
                message = ex.Message
            });
        }
    }
}

public static class McpProxyMiddlewareExtensions
{
    public static IApplicationBuilder UseMcpProxy(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<McpProxyMiddleware>();
    }
}
