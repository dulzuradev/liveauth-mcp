using System.Net;
using System.Text.Json;
using FluentAssertions;
using LiveAuthCore.Middleware;
using LiveAuthCore.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace LiveAuthCore.Tests.Middleware;

public class L402MiddlewareTests
{
    [Theory]
    [InlineData("/api/public/l402/invoice")]
    [InlineData("/api/auth/start")]
    [InlineData("/api/health")]
    [InlineData("/api/dev/projects")]
    [InlineData("/api/login")]
    [InlineData("/api/sats/balance")]
    public async Task InvokeAsync_ExcludedPath_CallsNextEvenWhenGatedPrefixWouldMatch(string path)
    {
        var nextCalled = false;
        var services = CreateServices(gatedPaths: "/api");
        var middleware = new L402Middleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateContext(path, services);

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_NonGatedPath_CallsNextWithoutToken()
    {
        var nextCalled = false;
        var services = CreateServices(gatedPaths: "/paid");
        var middleware = new L402Middleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateContext("/open/resource", services);

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_BlankConfiguredGatedPaths_FallsBackToUngatedDefault()
    {
        var nextCalled = false;
        var services = CreateServices(gatedPaths: "   ");
        var middleware = new L402Middleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateContext("/paid/resource", services);

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_MissingConfiguration_FallsBackToUngatedDefault()
    {
        var nextCalled = false;
        var services = CreateServices(gatedPaths: "/paid", registerConfiguration: false);
        var middleware = new L402Middleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateContext("/paid/resource", services);

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_GatedPathWithoutToken_ReturnsPaymentRequired()
    {
        var nextCalled = false;
        var services = CreateServices(gatedPaths: "/paid");
        var middleware = new L402Middleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateContext("/paid/resource", services);

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.PaymentRequired);
        context.Response.Headers.WWWAuthenticate.ToString().Should().Contain("L402");
        context.Response.Headers.WWWAuthenticate.ToString().Should().Contain("x402");
        var payload = await ReadJsonResponse(context);
        payload.GetProperty("error").GetString().Should().Be("Payment Required");
        payload.GetProperty("code").GetString().Should().Be("PAYMENT_REQUIRED");
        payload.GetProperty("schemes").EnumerateArray()
            .Select(scheme => scheme.GetString())
            .Should().Equal("L402", "x402");
    }

    [Fact]
    public async Task InvokeAsync_GatedPathWithInvalidL402Token_ReturnsPaymentRequired()
    {
        var nextCalled = false;
        var services = CreateServices(gatedPaths: "/paid");
        var middleware = new L402Middleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateContext("/paid/resource", services);
        context.Request.Headers.Authorization = "L402 invalid-token";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.PaymentRequired);
    }

    [Fact]
    public async Task InvokeAsync_GatedPathWithValidAuthorizationL402Token_ConsumesTokenAndCallsNext()
    {
        var nextCalled = false;
        var services = CreateServices(gatedPaths: "/paid");
        var token = await services.L402.IssueTokenAsync(new string('b', 64));
        token.Should().NotBeNullOrWhiteSpace();
        services.L402.IsTokenValid(token).Should().BeTrue();
        var middleware = new L402Middleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateContext("/paid/resource", services);
        context.Request.Headers.Authorization = $"L402 {token}";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        services.L402.GetRemainingTokenCalls(token).Should().Be(0);
    }

    [Fact]
    public async Task InvokeAsync_GatedPathWithValidHeaderL402Token_ConsumesTokenAndCallsNext()
    {
        var nextCalled = false;
        var services = CreateServices(gatedPaths: "/paid");
        var token = await services.L402.IssueTokenAsync(new string('c', 64));
        token.Should().NotBeNullOrWhiteSpace();
        services.L402.IsTokenValid(token).Should().BeTrue();
        var middleware = new L402Middleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateContext("/paid/resource", services);
        context.Request.Headers["X-L402-Token"] = token;

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        services.L402.GetRemainingTokenCalls(token).Should().Be(0);
    }

    [Fact]
    public async Task InvokeAsync_GatedPathWithInvalidX402Token_ReturnsPaymentRequired()
    {
        var nextCalled = false;
        var services = CreateServices(gatedPaths: "/paid");
        var middleware = new L402Middleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateContext("/paid/resource", services);
        context.Request.Headers.Authorization = "x402 short";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.PaymentRequired);
    }

    [Fact]
    public async Task InvokeAsync_GatedPathWithInvalidX402Hex_ReturnsPaymentRequired()
    {
        var nextCalled = false;
        var services = CreateServices(gatedPaths: "/paid");
        var middleware = new L402Middleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateContext("/paid/resource", services);
        context.Request.Headers.Authorization = $"x402 {new string('g', 64)}";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.PaymentRequired);
    }

    [Fact]
    public async Task InvokeAsync_GatedPathWithValidX402Token_CallsNext()
    {
        var nextCalled = false;
        var services = CreateServices(gatedPaths: "/paid");
        var middleware = new L402Middleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateContext("/paid/resource", services);
        context.Request.Headers.Authorization = $"x402 {new string('a', 64)}";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_GatedPathWithXPaymentInvoice_ReturnsPaymentRequired()
    {
        var nextCalled = false;
        var services = CreateServices(gatedPaths: "/paid");
        var middleware = new L402Middleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateContext("/paid/resource", services);
        context.Request.Headers["X-Payment"] = "lnbc1invoice";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.PaymentRequired);
    }

    [Fact]
    public async Task InvokeAsync_DevelopmentSkipHeaderOnGatedPath_CallsNextWithoutToken()
    {
        var nextCalled = false;
        var services = CreateServices(gatedPaths: "/paid", environmentName: "Development");
        var middleware = new L402Middleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateContext("/paid/resource", services);
        context.Request.Headers["X-Dev-Mode"] = "skip";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_GatedPathsCanComeFromConfigurationChildren()
    {
        var nextCalled = false;
        var services = CreateServices(new Dictionary<string, string?>
        {
            ["L402:GatedPaths:0"] = "/premium",
            ["L402:GatedPaths:1"] = "/metered"
        });
        var middleware = new L402Middleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateContext("/metered/resource", services);

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.PaymentRequired);
    }

    private static MiddlewareServices CreateServices(
        string? gatedPaths = null,
        string? environmentName = null,
        bool registerConfiguration = true)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Lnd:UseMock"] = "true",
            ["L402:TokenTtlMinutes"] = "60"
        };

        if (gatedPaths != null)
            settings["L402:GatedPaths"] = gatedPaths;

        return CreateServices(settings, environmentName, registerConfiguration);
    }

    private static MiddlewareServices CreateServices(
        Dictionary<string, string?> settings,
        string? environmentName = null,
        bool registerConfiguration = true)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var lightning = new LightningService(configuration);
        var l402 = new L402Service(
            lightning,
            new MemoryCache(new MemoryCacheOptions()),
            configuration);
        var serviceCollection = new ServiceCollection()
            .AddLogging()
            .AddSingleton(lightning)
            .AddSingleton(l402);

        if (registerConfiguration)
            serviceCollection.AddSingleton<IConfiguration>(configuration);

        if (environmentName != null)
            serviceCollection.AddSingleton<IWebHostEnvironment>(new TestWebHostEnvironment(environmentName));

        return new MiddlewareServices(l402, serviceCollection.BuildServiceProvider());
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
        L402Service L402,
        IServiceProvider RequestServices);

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public TestWebHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string ApplicationName { get; set; } = "LiveAuthCore.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = "";
        public string EnvironmentName { get; set; }
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
