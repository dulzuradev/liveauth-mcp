using System.Net;
using System.Text.Json;
using FluentAssertions;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Middleware;
using LiveAuthCore.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LiveAuthCore.Tests.Middleware;

public class PublicKeyAuthMiddlewareTests
{
    [Theory]
    [InlineData("/api/admin/status")]
    [InlineData("/api/public/auth/start")]
    [InlineData("/api/public/pow/challenge")]
    [InlineData("/api/public/l402/invoice")]
    [InlineData("/api/dev/projects")]
    [InlineData("/api/health")]
    public async Task InvokeAsync_BypassPath_CallsNextWithoutApiKey(string path)
    {
        var nextCalled = false;
        var middleware = new PublicKeyAuthMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var services = CreateServices();
        var context = CreateContext(path, services);

        await middleware.InvokeAsync(context, services.ApiKeys, new BillingService(), services.Db);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_OptionsRequest_CallsNextWithoutApiKey()
    {
        var nextCalled = false;
        var middleware = new PublicKeyAuthMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var services = CreateServices();
        var context = CreateContext("/api/private/resource", services);
        context.Request.Method = HttpMethods.Options;

        await middleware.InvokeAsync(context, services.ApiKeys, new BillingService(), services.Db);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_MissingPublicKey_ReturnsUnauthorizedAndDoesNotCallNext()
    {
        var nextCalled = false;
        var middleware = new PublicKeyAuthMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var services = CreateServices();
        var context = CreateContext("/api/private/resource", services);

        await middleware.InvokeAsync(context, services.ApiKeys, new BillingService(), services.Db);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.Unauthorized);
        var payload = await ReadJsonResponse(context);
        payload.GetProperty("error").GetString().Should().Be("missing_api_key");
        payload.GetProperty("error_description").GetString().Should().Be("Missing public API key header.");
    }

    [Fact]
    public async Task InvokeAsync_BlankPublicKey_ReturnsUnauthorizedAndDoesNotCallNext()
    {
        var nextCalled = false;
        var middleware = new PublicKeyAuthMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var services = CreateServices();
        var context = CreateContext("/api/private/resource", services);
        context.Request.Headers["X-LW-Public"] = "   ";

        await middleware.InvokeAsync(context, services.ApiKeys, new BillingService(), services.Db);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.Unauthorized);
        var payload = await ReadJsonResponse(context);
        payload.GetProperty("error").GetString().Should().Be("invalid_api_key");
        payload.GetProperty("error_description").GetString().Should().Be("Public API key cannot be empty.");
    }

    [Fact]
    public async Task InvokeAsync_InvalidPublicKey_ReturnsUnauthorizedAndDoesNotCallNext()
    {
        var nextCalled = false;
        var middleware = new PublicKeyAuthMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var services = CreateServices();
        var context = CreateContext("/api/private/resource", services);
        context.Request.Headers["X-LW-Public"] = "not-a-liveauth-key";

        await middleware.InvokeAsync(context, services.ApiKeys, new BillingService(), services.Db);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.Unauthorized);
        var payload = await ReadJsonResponse(context);
        payload.GetProperty("error").GetString().Should().Be("invalid_api_key");
    }

    [Fact]
    public async Task InvokeAsync_ValidProjectPublicKey_BindsProjectAndCallsNext()
    {
        var nextCalled = false;
        var project = CreateProject(publicKey: "la_pk_live_project");
        var services = CreateServices(project);
        var middleware = new PublicKeyAuthMiddleware(context =>
        {
            nextCalled = true;
            context.Items["next-observed-project"] = context.Items[HttpContextKeys.Project];
            return Task.CompletedTask;
        });
        var context = CreateContext("/api/private/resource", services);
        context.Request.Headers["X-LW-Public"] = project.PublicKey;

        await middleware.InvokeAsync(context, services.ApiKeys, new BillingService(), services.Db);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Items[HttpContextKeys.Project].Should().BeSameAs(project);
        context.Items["next-observed-project"].Should().BeSameAs(project);
    }

    [Fact]
    public async Task InvokeAsync_ValidApiKeyPublicKey_BindsProjectAndUpdatesLastUsed()
    {
        var project = CreateProject(publicKey: "la_pk_project_primary");
        var apiKey = new ProjectApiKey
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Project = project,
            PublicKey = "la_pk_browser_key",
            SecretKeyHash = "unused",
            Label = "browser",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var services = CreateServices(project, apiKey);
        var middleware = new PublicKeyAuthMiddleware(_ => Task.CompletedTask);
        var context = CreateContext("/api/private/resource", services);
        context.Request.Headers["X-LW-Public"] = apiKey.PublicKey;

        await middleware.InvokeAsync(context, services.ApiKeys, new BillingService(), services.Db);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Items[HttpContextKeys.Project].Should().BeSameAs(project);
        apiKey.LastUsedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task InvokeAsync_ExpiredProProject_DowngradesPlanBeforeCallingNext()
    {
        var project = CreateProject(
            publicKey: "la_pk_expired_pro",
            plan: "pro",
            proPaidUntil: DateTime.UtcNow.AddDays(-8),
            monthlyQuota: PlanLimits.ProMonthlyAuthLimit);
        var services = CreateServices(project);
        var middleware = new PublicKeyAuthMiddleware(_ => Task.CompletedTask);
        var context = CreateContext("/api/private/resource", services);
        context.Request.Headers["X-LW-Public"] = project.PublicKey;

        await middleware.InvokeAsync(context, services.ApiKeys, new BillingService(), services.Db);

        project.Plan.Should().Be("free");
        project.ProPaidUntil.Should().BeNull();
        project.MonthlyQuota.Should().Be(PlanLimits.FreeMonthlyAuthLimit);
        var savedProject = await services.Db.Projects.SingleAsync(p => p.Id == project.Id);
        savedProject.Plan.Should().Be("free");
    }

    private static MiddlewareServices CreateServices(params object[] seedEntities)
    {
        var db = CreateDbContext();
        foreach (var entity in seedEntities)
            db.Add(entity);
        db.SaveChanges();

        var httpContextAccessor = new HttpContextAccessor();
        var authEvents = new AuthEventService(db, httpContextAccessor);
        var apiKeys = new ApiKeyService(db, authEvents);
        var requestServices = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        return new MiddlewareServices(db, apiKeys, requestServices);
    }

    private static LiveAuthDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<LiveAuthDbContext>()
            .UseInMemoryDatabase($"PublicKeyMiddlewareTests_{Guid.NewGuid():N}")
            .Options;

        return new LiveAuthDbContext(options);
    }

    private static Project CreateProject(
        string publicKey,
        string plan = "free",
        DateTime? proPaidUntil = null,
        long monthlyQuota = PlanLimits.FreeMonthlyAuthLimit)
    {
        return new Project
        {
            Id = Guid.NewGuid(),
            DeveloperId = Guid.NewGuid(),
            Name = "Middleware Project",
            PublicKey = publicKey,
            SecretKeyHash = "unused",
            IsActive = true,
            Environment = "LIVE",
            AllowDemoAuth = false,
            Plan = plan,
            ProPaidUntil = proPaidUntil,
            MonthlyQuota = monthlyQuota,
            CreatedAt = DateTime.UtcNow
        };
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
        ApiKeyService ApiKeys,
        IServiceProvider RequestServices);
}
