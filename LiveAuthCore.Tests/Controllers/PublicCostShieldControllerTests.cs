using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Models.CostShield;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LiveAuthCore.Tests.Controllers;

public sealed class PublicCostShieldControllerTests
    : IClassFixture<LiveAuthWebApplicationFactory>
{
    private const string AllowedOrigin = "https://app.example.com";
    private readonly HttpClient _client;
    private readonly LiveAuthWebApplicationFactory _factory;

    public PublicCostShieldControllerTests(LiveAuthWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CostShield_preflight_allows_external_browser_clients()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Options,
            "/api/public/costshield/challenges");
        request.Headers.Add("Origin", "https://customer.example");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add(
            "Access-Control-Request-Headers",
            "content-type,x-lw-public");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin")
            .Should().ContainSingle("*");
        response.Headers.GetValues("Access-Control-Allow-Methods")
            .Single()
            .Should().Contain("POST");
        response.Headers.GetValues("Access-Control-Allow-Headers")
            .Single()
            .Should().Contain("X-LW-Public");
    }

    [Fact]
    public async Task Dashboard_preflight_keeps_the_restricted_origin_policy()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Options,
            "/api/dev/projects");
        request.Headers.Add("Origin", "https://liveauth.app");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add(
            "Access-Control-Request-Headers",
            "authorization");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin")
            .Should().ContainSingle("https://liveauth.app");
        response.Headers.GetValues("Access-Control-Allow-Credentials")
            .Should().ContainSingle("true");
    }

    [Fact]
    public async Task ChallengeToConsumeFlow_IssuesVerifiesAndConsumesSingleUseToken()
    {
        var seed = await SeedActionAsync();
        var challenge = await CreateChallengeAsync(seed.PublicKey, AllowedOrigin);
        var nonce = Solve(seed.PublicKey, challenge);
        var authorization = await CompleteChallengeAsync(
            seed.PublicKey,
            challenge,
            nonce,
            AllowedOrigin);

        authorization.RequireSingleUse.Should().BeTrue();
        authorization.Token.Should().NotBeNullOrWhiteSpace();

        var verify = await SendAuthorizationRequestAsync(
            "verify",
            seed.SecretKey,
            authorization.Token,
            AllowedOrigin);
        verify.StatusCode.Should().Be(HttpStatusCode.OK);
        var verified = await verify.Content
            .ReadFromJsonAsync<VerifyCostShieldAuthorizationResponse>();
        verified.Should().NotBeNull();
        verified!.Verified.Should().BeTrue();
        verified.Consumed.Should().BeFalse();

        var consume = await SendAuthorizationRequestAsync(
            "consume",
            seed.SecretKey,
            authorization.Token,
            AllowedOrigin);
        consume.StatusCode.Should().Be(HttpStatusCode.OK);
        var consumed = await consume.Content
            .ReadFromJsonAsync<VerifyCostShieldAuthorizationResponse>();
        consumed.Should().NotBeNull();
        consumed!.Consumed.Should().BeTrue();

        var secondConsume = await SendAuthorizationRequestAsync(
            "consume",
            seed.SecretKey,
            authorization.Token,
            AllowedOrigin);
        secondConsume.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await secondConsume.Content.ReadAsStringAsync())
            .Should().Contain("authorization_already_consumed");

        var replay = await CompleteChallengeResponseAsync(
            seed.PublicKey,
            challenge,
            nonce,
            AllowedOrigin);
        replay.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await replay.Content.ReadAsStringAsync())
            .Should().Contain("challenge_replayed");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        var persisted = await db.CostShieldAuthorizations
            .SingleAsync(item => item.Id == authorization.AuthorizationId);
        persisted.Status.Should().Be(CostShieldAuthorizationStatuses.Consumed);
        persisted.ConsumedAt.Should().NotBeNull();

        var events = await db.AuthEvents
            .Where(item => item.ProtectedActionId == seed.ActionId)
            .ToListAsync();
        events.Should().Contain(item =>
            item.EventType == AuthEventType.CostShieldAuthorizationConsumed);
        events.Should().OnlyContain(item => item.ClientIp == null);
        events.Should().OnlyContain(item =>
            !string.IsNullOrWhiteSpace(item.IpAddressHash));
    }

    [Fact]
    public async Task Challenge_InvalidOrigin_IsRejectedWithoutIssuingChallenge()
    {
        var seed = await SeedActionAsync();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/public/costshield/challenges");
        request.Headers.Add("X-LW-Public", seed.PublicKey);
        request.Headers.Add("Origin", "https://attacker.example");
        request.Content = JsonContent.Create(new CreateCostShieldChallengeRequest
        {
            Environment = "TEST",
            Action = "ai.generate_image",
            Origin = "https://attacker.example"
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("invalid_origin");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        (await db.CostShieldAuthorizations.CountAsync(item =>
            item.ProtectedActionId == seed.ActionId)).Should().Be(0);
    }

    [Fact]
    public async Task Verify_ExpectedActionMismatchOrWrongProjectSecret_IsRejected()
    {
        var seed = await SeedActionAsync();
        var other = await SeedActionAsync("ai.other_action");
        var challenge = await CreateChallengeAsync(seed.PublicKey, AllowedOrigin);
        var authorization = await CompleteChallengeAsync(
            seed.PublicKey,
            challenge,
            Solve(seed.PublicKey, challenge),
            AllowedOrigin);

        var mismatch = await SendAuthorizationRequestAsync(
            "verify",
            seed.SecretKey,
            authorization.Token,
            AllowedOrigin,
            expectedAction: "ai.other_action");
        mismatch.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await mismatch.Content.ReadAsStringAsync()).Should().Contain("action_mismatch");

        var wrongEnvironment = await SendAuthorizationRequestAsync(
            "verify",
            seed.SecretKey,
            authorization.Token,
            AllowedOrigin,
            expectedEnvironment: "LIVE");
        wrongEnvironment.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await wrongEnvironment.Content.ReadAsStringAsync())
            .Should().Contain("environment_mismatch");

        var tokenParts = authorization.Token.Split('.');
        tokenParts.Should().HaveCount(3);
        tokenParts[2] =
            (tokenParts[2][0] == 'a' ? 'b' : 'a') +
            tokenParts[2][1..];
        var tamperedToken = string.Join('.', tokenParts);
        var tampered = await SendAuthorizationRequestAsync(
            "verify",
            seed.SecretKey,
            tamperedToken,
            AllowedOrigin);
        tampered.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await tampered.Content.ReadAsStringAsync()).Should().Contain("invalid_token");

        var wrongProject = await SendAuthorizationRequestAsync(
            "verify",
            other.SecretKey,
            authorization.Token,
            AllowedOrigin);
        wrongProject.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await wrongProject.Content.ReadAsStringAsync()).Should().Contain("invalid_token");
    }

    [Fact]
    public async Task Jwks_ExposesPublicRsaSigningKey()
    {
        var response = await _client.GetAsync(
            "/api/public/costshield/.well-known/jwks.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var jwks = await response.Content.ReadFromJsonAsync<CostShieldJwksResponse>();
        jwks.Should().NotBeNull();
        jwks!.Keys.Should().ContainSingle();
        jwks.Keys[0].Kty.Should().Be("RSA");
        jwks.Keys[0].Alg.Should().Be("RS256");
        jwks.Keys[0].N.Should().NotBeNullOrWhiteSpace();
        jwks.Keys[0].E.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Completion_InvalidPowModifiedOrExpiredChallenge_IsRejected()
    {
        var seed = await SeedActionAsync();
        var challenge = await CreateChallengeAsync(seed.PublicKey, AllowedOrigin);
        var solvedNonce = Solve(seed.PublicKey, challenge);
        var invalidNonce = FindInvalidNonce(
            seed.PublicKey,
            challenge,
            solvedNonce + 1);

        var invalidPow = await CompleteChallengeResponseAsync(
            seed.PublicKey,
            challenge,
            invalidNonce,
            AllowedOrigin);
        invalidPow.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await invalidPow.Content.ReadAsStringAsync()).Should().Contain("invalid_pow");

        var modified = challenge with
        {
            ConfigurationVersion = challenge.ConfigurationVersion + 1
        };
        var modifiedResponse = await CompleteChallengeResponseAsync(
            seed.PublicKey,
            modified,
            solvedNonce,
            AllowedOrigin);
        modifiedResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await modifiedResponse.Content.ReadAsStringAsync())
            .Should().Contain("invalid_challenge");

        var expired = challenge with
        {
            ExpiresAtUnix = DateTimeOffset.UtcNow.AddSeconds(-1).ToUnixTimeSeconds()
        };
        var expiredResponse = await CompleteChallengeResponseAsync(
            seed.PublicKey,
            expired,
            solvedNonce,
            AllowedOrigin);
        expiredResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await expiredResponse.Content.ReadAsStringAsync())
            .Should().Contain("challenge_expired");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        (await db.CostShieldAuthorizations.CountAsync(item =>
            item.ProtectedActionId == seed.ActionId)).Should().Be(0);
    }

    [Fact]
    public async Task Challenge_HighRiskUsesMaximumDifficulty_AndActionLimitIsEnforced()
    {
        var seed = await SeedActionAsync(
            anonymousRequestLimit: 1,
            baseDifficulty: 8,
            suspiciousDifficulty: 10,
            maximumDifficulty: 12);
        var challenge = await CreateChallengeAsync(
            seed.PublicKey,
            AllowedOrigin,
            riskHint: "high");

        challenge.DifficultyBits.Should().Be(12);
        challenge.DifficultyReason.Should().Be("explicit_high_risk");

        using var request = CreateChallengeRequest(
            seed.PublicKey,
            AllowedOrigin,
            riskHint: null);
        var rateLimited = await _client.SendAsync(request);
        rateLimited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        rateLimited.Headers.RetryAfter.Should().NotBeNull();
        (await rateLimited.Content.ReadAsStringAsync())
            .Should().Contain("action_rate_limit");
    }

    [Fact]
    public async Task Verify_PersistedAuthorizationExpiryIsEnforced()
    {
        var seed = await SeedActionAsync();
        var challenge = await CreateChallengeAsync(seed.PublicKey, AllowedOrigin);
        var authorization = await CompleteChallengeAsync(
            seed.PublicKey,
            challenge,
            Solve(seed.PublicKey, challenge),
            AllowedOrigin);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
            var persisted = await db.CostShieldAuthorizations
                .SingleAsync(item => item.Id == authorization.AuthorizationId);
            persisted.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }

        var verify = await SendAuthorizationRequestAsync(
            "verify",
            seed.SecretKey,
            authorization.Token,
            AllowedOrigin);
        verify.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await verify.Content.ReadAsStringAsync()).Should().Contain("token_expired");
    }

    [Fact]
    public async Task Challenge_WrongEnvironmentIsRejected()
    {
        var seed = await SeedActionAsync();
        using var request = CreateChallengeRequest(
            seed.PublicKey,
            AllowedOrigin,
            environment: "LIVE");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync())
            .Should().Contain("environment_mismatch");
    }

    [Fact]
    public async Task Challenge_RotatingClientSubjectsCannotBypassIpLimit()
    {
        var seed = await SeedActionAsync(
            anonymousRequestLimit: 1,
            authenticatedRequestLimit: 100);

        using var firstRequest = CreateChallengeRequest(
            seed.PublicKey,
            AllowedOrigin,
            subject: "user-one");
        var first = await _client.SendAsync(firstRequest);
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        using var secondRequest = CreateChallengeRequest(
            seed.PublicKey,
            AllowedOrigin,
            subject: "user-two");
        var second = await _client.SendAsync(secondRequest);
        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await second.Content.ReadAsStringAsync())
            .Should().Contain("action_rate_limit");
    }

    private async Task<CostShieldChallengeResponse> CreateChallengeAsync(
        string publicKey,
        string origin,
        string? riskHint = null)
    {
        using var request = CreateChallengeRequest(
            publicKey,
            origin,
            riskHint);
        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var challenge = await response.Content
            .ReadFromJsonAsync<CostShieldChallengeResponse>();
        challenge.Should().NotBeNull();
        return challenge!;
    }

    private static HttpRequestMessage CreateChallengeRequest(
        string publicKey,
        string origin,
        string? riskHint = null,
        string environment = "TEST",
        string? subject = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/public/costshield/challenges");
        request.Headers.Add("X-LW-Public", publicKey);
        request.Headers.Add("Origin", origin);
        request.Content = JsonContent.Create(new CreateCostShieldChallengeRequest
        {
            Environment = environment,
            Action = "ai.generate_image",
            Origin = origin,
            RiskHint = riskHint,
            Subject = subject
        });
        return request;
    }

    private async Task<CostShieldAuthorizationResponse> CompleteChallengeAsync(
        string publicKey,
        CostShieldChallengeResponse challenge,
        long nonce,
        string origin)
    {
        var response = await CompleteChallengeResponseAsync(
            publicKey,
            challenge,
            nonce,
            origin);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var authorization = await response.Content
            .ReadFromJsonAsync<CostShieldAuthorizationResponse>();
        authorization.Should().NotBeNull();
        return authorization!;
    }

    private Task<HttpResponseMessage> CompleteChallengeResponseAsync(
        string publicKey,
        CostShieldChallengeResponse challenge,
        long nonce,
        string origin)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/public/costshield/challenges/{challenge.ChallengeId}/complete");
        request.Headers.Add("X-LW-Public", publicKey);
        request.Headers.Add("Origin", origin);
        request.Content = JsonContent.Create(new CompleteCostShieldChallengeRequest
        {
            Environment = challenge.Environment,
            Action = challenge.Action,
            Origin = origin,
            Nonce = nonce,
            DifficultyBits = challenge.DifficultyBits,
            ExpiresAtUnix = challenge.ExpiresAtUnix,
            ConfigurationVersion = challenge.ConfigurationVersion,
            Signature = challenge.Signature
        });
        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> SendAuthorizationRequestAsync(
        string operation,
        string secretKey,
        string token,
        string origin,
        string expectedAction = "ai.generate_image",
        string expectedEnvironment = "TEST")
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/costshield/authorizations/{operation}");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            secretKey);
        request.Content = JsonContent.Create(new VerifyCostShieldAuthorizationRequest
        {
            Token = token,
            Action = expectedAction,
            Environment = expectedEnvironment,
            Origin = origin
        });
        return _client.SendAsync(request);
    }

    private async Task<CostShieldTestSeed> SeedActionAsync(
        string actionName = "ai.generate_image",
        int anonymousRequestLimit = 100,
        int baseDifficulty = 8,
        int suspiciousDifficulty = 8,
        int maximumDifficulty = 8,
        int? authenticatedRequestLimit = null)
    {
        var developerId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var publicKey = $"la_pk_{projectId:N}";
        var secretKey = $"la_sk_{Guid.NewGuid():N}";
        var project = new Project
        {
            Id = projectId,
            DeveloperId = developerId,
            Name = "CostShield flow test",
            PublicKey = publicKey,
            IsActive = true,
            Plan = "free",
            Environment = "TEST",
            CreatedAt = DateTime.UtcNow
        };
        project.SecretKeyHash =
            new PasswordHasher<Project>().HashPassword(project, secretKey);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        db.Developers.Add(new Developer
        {
            Id = developerId,
            Email = $"{developerId:N}@flow.costshield.test",
            CreatedAt = DateTime.UtcNow
        });
        db.Projects.Add(project);
        db.ProtectedActions.Add(new ProtectedAction
        {
            Id = actionId,
            ProjectId = projectId,
            Environment = "TEST",
            Name = actionName,
            DisplayName = "Generate Image",
            Description = "Protect an expensive operation.",
            IsEnabled = true,
            BaseDifficulty = baseDifficulty,
            SuspiciousDifficulty = suspiciousDifficulty,
            MaximumDifficulty = maximumDifficulty,
            AnonymousRequestLimit = anonymousRequestLimit,
            AnonymousLimitWindowSeconds = 60,
            AuthenticatedRequestLimit = authenticatedRequestLimit,
            AuthenticatedLimitWindowSeconds =
                authenticatedRequestLimit.HasValue ? 60 : null,
            RequireSingleUseToken = true,
            TokenLifetimeSeconds = 120,
            AllowedOrigins = new List<string> { AllowedOrigin },
            FailureBehavior = ProtectedActionFailureBehaviors.Deny,
            EstimatedCostPerExecution = 0.02m,
            ConfigurationVersion = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        return new CostShieldTestSeed(
            projectId,
            actionId,
            publicKey,
            secretKey);
    }

    private static long Solve(
        string publicKey,
        CostShieldChallengeResponse challenge)
    {
        var target = Convert.FromHexString(challenge.TargetHex);
        for (long nonce = 0; nonce < long.MaxValue; nonce++)
        {
            var hash = SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    $"{publicKey}:{challenge.ChallengeId}:{nonce}"));
            if (PowDifficulty.IsValid(hash, target))
                return nonce;
        }

        throw new InvalidOperationException("Unable to solve CostShield challenge.");
    }

    private static long FindInvalidNonce(
        string publicKey,
        CostShieldChallengeResponse challenge,
        long startingNonce)
    {
        var target = Convert.FromHexString(challenge.TargetHex);
        for (var nonce = startingNonce; nonce < long.MaxValue; nonce++)
        {
            var hash = SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    $"{publicKey}:{challenge.ChallengeId}:{nonce}"));
            if (!PowDifficulty.IsValid(hash, target))
                return nonce;
        }

        throw new InvalidOperationException("Unable to find an invalid CostShield nonce.");
    }

    private sealed record CostShieldTestSeed(
        Guid ProjectId,
        Guid ActionId,
        string PublicKey,
        string SecretKey);
}
