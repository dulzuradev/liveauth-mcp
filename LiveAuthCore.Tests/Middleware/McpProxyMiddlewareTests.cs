using System.Net;
using System.Text.Json;
using FluentAssertions;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Middleware;
using LiveAuthCore.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LiveAuthCore.Tests.Middleware;

public class McpProxyMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_NonMcpPath_CallsNext()
    {
        var nextCalled = false;
        var middleware = new McpProxyMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var services = CreateServices();
        var context = CreateContext("/api/health", services);

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_McpPathWithoutRegisteredProxy_ReturnsNotFound()
    {
        var nextCalled = false;
        var middleware = new McpProxyMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var services = CreateServices();
        var context = CreateContext("/mcp/missing-proxy", services);

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
        var payload = await ReadJsonResponse(context);
        payload.GetProperty("error").GetString().Should().Be("Proxy not found");
    }

    [Fact]
    public async Task InvokeAsync_RegisteredProxyWithoutToken_ReturnsPaymentRequiredInvoice()
    {
        var proxy = new McpProxy
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            Name = "Test MCP",
            UpstreamUrl = "https://upstream.example",
            CustomPath = "test-mcp",
            SatsPerRequest = 7,
            IsActive = true
        };
        var services = CreateServices(proxy);
        var middleware = new McpProxyMiddleware(_ => Task.CompletedTask);
        var context = CreateContext("/mcp/test-mcp", services);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.PaymentRequired);
        context.Response.Headers.WWWAuthenticate.ToString().Should().Contain("L402");
        var payload = await ReadJsonResponse(context);
        payload.GetProperty("error").GetString().Should().Be("Payment required");
        payload.GetProperty("mode").GetString().Should().Be("l402");
        payload.GetProperty("invoice").GetString().Should().Be("lnmock1devlogininvoice");
        payload.GetProperty("amountSats").GetInt32().Should().Be(7);
        payload.GetProperty("proxy").GetString().Should().Be("Test MCP");

        var savedProxy = await services.Db.McpProxies.SingleAsync(p => p.Id == proxy.Id);
        savedProxy.TotalRequests.Should().Be(1);
        savedProxy.TotalSatsEarned.Should().Be(0);
    }

    private static MiddlewareServices CreateServices(params object[] seedEntities)
    {
        var db = CreateDbContext();
        foreach (var entity in seedEntities)
            db.Add(entity);
        db.SaveChanges();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lnd:UseMock"] = "true",
                ["L402:TokenTtlMinutes"] = "60"
            })
            .Build();
        var l402 = new L402Service(
            new LightningService(configuration),
            new MemoryCache(new MemoryCacheOptions()),
            configuration);
        var requestServices = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton(db)
            .AddSingleton(l402)
            .BuildServiceProvider();

        return new MiddlewareServices(db, requestServices);
    }

    private static LiveAuthDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<LiveAuthDbContext>()
            .UseInMemoryDatabase($"McpProxyMiddlewareTests_{Guid.NewGuid():N}")
            .Options;

        return new LiveAuthDbContext(options);
    }

    private static DefaultHttpContext CreateContext(string path, MiddlewareServices services)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = services.RequestServices
        };
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<JsonElement> ReadJsonResponse(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var doc = JsonDocument.Parse(await new StreamReader(context.Response.Body).ReadToEndAsync());
        return doc.RootElement.Clone();
    }

    private sealed record MiddlewareServices(
        LiveAuthDbContext Db,
        IServiceProvider RequestServices);
}
