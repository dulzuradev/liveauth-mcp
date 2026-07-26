using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace LiveAuth.CostShield.AspNetCore.Tests;

public sealed class LiveAuthCostShieldVerifierTests
{
    private static readonly Guid ProjectId =
        Guid.Parse("b2bab5ec-9bb0-4054-b448-563ba2113e5a");
    private const string Action = "generate-report";
    private const string Origin = "https://app.example.com";

    [Fact]
    public async Task VerifyAsync_ValidReusableToken_UsesCachedJwks()
    {
        var signing = CreateSigningMaterial();
        var handler = new RecordingHandler();
        handler.EnqueueJson(signing.Jwks);
        await using var provider = CreateProvider(handler);
        using var scope = provider.CreateScope();
        var verifier = scope.ServiceProvider.GetRequiredService<
            ILiveAuthCostShieldVerifier>();

        var first = await verifier.VerifyAsync(
            signing.Token,
            Action,
            Origin);
        var second = await verifier.VerifyAsync(
            signing.Token,
            Action,
            Origin);

        first.ProjectId.Should().Be(ProjectId);
        first.Action.Should().Be(Action);
        first.Origin.Should().Be(Origin);
        first.SingleUse.Should().BeFalse();
        second.TokenId.Should().Be(first.TokenId);
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        handler.Requests[0].Path.Should().Be(
            "/api/public/costshield/.well-known/jwks.json");
    }

    [Fact]
    public async Task VerifyAsync_WrongAction_IsForbidden()
    {
        var signing = CreateSigningMaterial();
        var handler = new RecordingHandler();
        handler.EnqueueJson(signing.Jwks);
        await using var provider = CreateProvider(handler);
        using var scope = provider.CreateScope();
        var verifier = scope.ServiceProvider.GetRequiredService<
            ILiveAuthCostShieldVerifier>();

        var act = () => verifier.VerifyAsync(
            signing.Token,
            "different-action",
            Origin);

        var assertion = await act.Should().ThrowAsync<
            LiveAuthCostShieldException>();
        assertion.Which.Code.Should().Be("action_mismatch");
        assertion.Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task VerifyAsync_ExpiredToken_ReturnsTokenExpired()
    {
        var signing = CreateSigningMaterial(expired: true);
        var handler = new RecordingHandler();
        handler.EnqueueJson(signing.Jwks);
        await using var provider = CreateProvider(handler);
        using var scope = provider.CreateScope();
        var verifier = scope.ServiceProvider.GetRequiredService<
            ILiveAuthCostShieldVerifier>();

        var act = () => verifier.VerifyAsync(
            signing.Token,
            Action,
            Origin);

        var assertion = await act.Should().ThrowAsync<
            LiveAuthCostShieldException>();
        assertion.Which.Code.Should().Be("token_expired");
        assertion.Which.StatusCode.Should().Be(
            HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task VerifyAsync_WrongProject_IsForbidden()
    {
        var signing = CreateSigningMaterial(
            tokenProjectId: Guid.NewGuid());
        var handler = new RecordingHandler();
        handler.EnqueueJson(signing.Jwks);
        await using var provider = CreateProvider(handler);
        using var scope = provider.CreateScope();
        var verifier = scope.ServiceProvider.GetRequiredService<
            ILiveAuthCostShieldVerifier>();

        var act = () => verifier.VerifyAsync(
            signing.Token,
            Action,
            Origin);

        var assertion = await act.Should().ThrowAsync<
            LiveAuthCostShieldException>();
        assertion.Which.Code.Should().Be("project_mismatch");
        assertion.Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task VerifyAsync_UnknownCachedKey_RefreshesJwks()
    {
        var stale = CreateSigningMaterial();
        var current = CreateSigningMaterial();
        var handler = new RecordingHandler();
        handler.EnqueueJson(stale.Jwks);
        handler.EnqueueJson(current.Jwks);
        await using var provider = CreateProvider(handler);
        using var scope = provider.CreateScope();
        var verifier = scope.ServiceProvider.GetRequiredService<
            ILiveAuthCostShieldVerifier>();

        var claims = await verifier.VerifyAsync(
            current.Token,
            Action,
            Origin);

        claims.ProjectId.Should().Be(ProjectId);
        handler.Requests.Should().HaveCount(2);
        handler.Requests.Should().OnlyContain(
            request => request.Method == HttpMethod.Get);
    }

    [Fact]
    public async Task AuthorizeAsync_SingleUseToken_ConsumesRemotely()
    {
        var signing = CreateSigningMaterial(singleUse: true);
        var handler = new RecordingHandler();
        handler.EnqueueJson(signing.Jwks);
        handler.EnqueueJson(new
        {
            verified = true,
            consumed = true,
            authorizationId =
                "0ea52b67-e6ec-4dfd-9390-046da276b87f",
            action = Action,
            environment = "TEST",
            origin = Origin,
            verificationMethod = "pow",
            expiresAtUnix = DateTimeOffset.UtcNow.AddMinutes(1)
                .ToUnixTimeSeconds(),
            requireSingleUse = true
        });
        await using var provider = CreateProvider(handler);
        using var scope = provider.CreateScope();
        var verifier = scope.ServiceProvider.GetRequiredService<
            ILiveAuthCostShieldVerifier>();

        var authorization = await verifier.AuthorizeAsync(
            signing.Token,
            Action,
            Origin);

        authorization.Remote.Should().NotBeNull();
        authorization.Remote!.Consumed.Should().BeTrue();
        handler.Requests.Should().HaveCount(2);
        var consume = handler.Requests[1];
        consume.Method.Should().Be(HttpMethod.Post);
        consume.Path.Should().Be(
            "/api/costshield/authorizations/consume");
        consume.Authorization.Should().Be("Bearer la_sk_test");
        using var json = JsonDocument.Parse(consume.Body!);
        json.RootElement.GetProperty("token").GetString()
            .Should().Be(signing.Token);
        json.RootElement.GetProperty("action").GetString()
            .Should().Be(Action);
    }

    [Fact]
    public async Task AuthorizeAsync_NeverConsume_RejectsSingleUseToken()
    {
        var signing = CreateSigningMaterial(singleUse: true);
        var handler = new RecordingHandler();
        handler.EnqueueJson(signing.Jwks);
        await using var provider = CreateProvider(handler);
        using var scope = provider.CreateScope();
        var verifier = scope.ServiceProvider.GetRequiredService<
            ILiveAuthCostShieldVerifier>();

        var act = () => verifier.AuthorizeAsync(
            signing.Token,
            Action,
            Origin,
            LiveAuthCostShieldConsumeMode.Never);

        var assertion = await act.Should().ThrowAsync<
            LiveAuthCostShieldException>();
        assertion.Which.Code.Should().Be(
            "single_use_requires_consumption");
        handler.Requests.Should().ContainSingle();
    }

    private static ServiceProvider CreateProvider(
        RecordingHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLiveAuthCostShield(options =>
        {
            options.ProjectId = ProjectId;
            options.Environment =
                LiveAuthCostShieldEnvironment.Test;
            options.SecretKey = "la_sk_test";
            options.ApiUrl = new Uri("https://api.example.test");
            options.Issuer = "https://issuer.example.test";
            options.Audience = "costshield-tests";
            options.ClockSkew = TimeSpan.Zero;
        });
        services
            .AddHttpClient(LiveAuthCostShieldDefaults.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });
    }

    private static SigningMaterial CreateSigningMaterial(
        bool singleUse = false,
        bool expired = false,
        Guid? tokenProjectId = null)
    {
        using var rsa = RSA.Create(2048);
        var kid = Guid.NewGuid().ToString("N");
        var key = new RsaSecurityKey(rsa)
        {
            KeyId = kid
        };
        var now = DateTimeOffset.UtcNow;
        var issuedAt = expired ? now.AddMinutes(-2) : now;
        var expiresAt = expired
            ? now.AddMinutes(-1)
            : now.AddMinutes(2);
        var claims = new[]
        {
            new Claim(
                JwtRegisteredClaimNames.Jti,
                Guid.NewGuid().ToString("D")),
            new Claim(
                JwtRegisteredClaimNames.Iat,
                issuedAt.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64),
            new Claim(
                "projectId",
                (tokenProjectId ?? ProjectId).ToString("D")),
            new Claim("projectPublicKey", "la_pk_test"),
            new Claim(
                "protectedActionId",
                Guid.NewGuid().ToString("D")),
            new Claim("environment", "TEST"),
            new Claim("action", Action),
            new Claim("origin", Origin),
            new Claim("verificationMethod", "pow"),
            new Claim(
                "difficulty",
                "18",
                ClaimValueTypes.Integer32),
            new Claim("clientContextHash", "context-hash"),
            new Claim(
                "singleUse",
                singleUse.ToString().ToLowerInvariant(),
                ClaimValueTypes.Boolean),
            new Claim(
                "configurationVersion",
                "3",
                ClaimValueTypes.Integer32)
        };
        var jwt = new JwtSecurityToken(
            issuer: "https://issuer.example.test",
            audience: "costshield-tests",
            claims: claims,
            notBefore: issuedAt.AddSeconds(-5).UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(
                key,
                SecurityAlgorithms.RsaSha256));
        jwt.Header["typ"] = LiveAuthCostShieldDefaults.TokenType;
        var token = new JwtSecurityTokenHandler().WriteToken(jwt);

        var parameters = rsa.ExportParameters(false);
        var jwks = new
        {
            keys = new[]
            {
                new
                {
                    kty = "RSA",
                    use = "sig",
                    kid,
                    alg = "RS256",
                    n = Base64UrlEncoder.Encode(parameters.Modulus),
                    e = Base64UrlEncoder.Encode(parameters.Exponent)
                }
            }
        };
        return new SigningMaterial(token, jwks);
    }

    private sealed record SigningMaterial(
        string Token,
        object Jwks);
}

public sealed class LiveAuthCostShieldAuthorizationFilterTests
{
    [Fact]
    public async Task OnAuthorizationAsync_MissingBearerToken_Returns401()
    {
        var filter = new LiveAuthCostShieldAuthorizationFilter(
            new StubVerifier(),
            "generate-report",
            null,
            LiveAuthCostShieldConsumeMode.Auto);
        var context = CreateContext();

        await filter.OnAuthorizationAsync(context);

        var result = context.Result.Should().BeOfType<ObjectResult>()
            .Subject;
        result.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task OnAuthorizationAsync_ValidBearerToken_SetsFeature()
    {
        var verifier = new StubVerifier();
        var filter = new LiveAuthCostShieldAuthorizationFilter(
            verifier,
            "generate-report",
            "https://app.example.com",
            LiveAuthCostShieldConsumeMode.Always);
        var context = CreateContext();
        context.HttpContext.Request.Headers.Authorization =
            "Bearer costshield-token";

        await filter.OnAuthorizationAsync(context);

        context.Result.Should().BeNull();
        verifier.Token.Should().Be("costshield-token");
        verifier.Action.Should().Be("generate-report");
        verifier.Origin.Should().Be("https://app.example.com");
        verifier.Consume.Should().Be(
            LiveAuthCostShieldConsumeMode.Always);
        context.HttpContext.GetLiveAuthCostShieldAuthorization()
            .Should().BeSameAs(verifier.Authorization);
    }

    private static AuthorizationFilterContext CreateContext()
        => new(
            new ActionContext(
                new DefaultHttpContext(),
                new RouteData(),
                new ActionDescriptor()),
            new List<IFilterMetadata>());

    private sealed class StubVerifier : ILiveAuthCostShieldVerifier
    {
        private readonly LiveAuthCostShieldClaims _claims = new(
            "token-id",
            Guid.NewGuid(),
            "la_pk_test",
            Guid.NewGuid(),
            "TEST",
            "generate-report",
            "https://app.example.com",
            "pow",
            18,
            "context-hash",
            false,
            1,
            null,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds(),
            new ClaimsPrincipal());

        public StubVerifier()
        {
            Authorization = new LiveAuthCostShieldAuthorization(
                _claims,
                null);
        }

        public string? Token { get; private set; }
        public string? Action { get; private set; }
        public string? Origin { get; private set; }
        public LiveAuthCostShieldConsumeMode Consume { get; private set; }
        public LiveAuthCostShieldAuthorization Authorization { get; }

        public Task<LiveAuthCostShieldClaims> VerifyAsync(
            string token,
            string action,
            string? origin = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_claims);

        public Task<LiveAuthCostShieldAuthorization> AuthorizeAsync(
            string token,
            string action,
            string? origin = null,
            LiveAuthCostShieldConsumeMode consume =
                LiveAuthCostShieldConsumeMode.Auto,
            CancellationToken cancellationToken = default)
        {
            Token = token;
            Action = action;
            Origin = origin;
            Consume = consume;
            return Task.FromResult(Authorization);
        }
    }
}

internal sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();

    public List<RecordedRequest> Requests { get; } = new();

    public void EnqueueJson(
        object body,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _responses.Enqueue(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json")
        });
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content == null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri?.AbsolutePath ?? string.Empty,
            request.Headers.Authorization?.ToString(),
            body));
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException(
                "No HTTP response was queued for the request.");
        }
        return _responses.Dequeue();
    }
}

internal sealed record RecordedRequest(
    HttpMethod Method,
    string Path,
    string? Authorization,
    string? Body);
