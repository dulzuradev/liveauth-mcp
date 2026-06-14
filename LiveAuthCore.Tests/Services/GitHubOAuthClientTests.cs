using System.Net;
using System.Text;
using FluentAssertions;
using LiveAuthCore.Services;
using Xunit;

namespace LiveAuthCore.Tests.Services;

public class GitHubOAuthClientTests
{
    [Fact]
    public async Task GetProfileAsync_ExchangesCodeAndReturnsPrimaryVerifiedEmail()
    {
        var requestedClients = new List<string>();
        var client = CreateClient((clientName, request) =>
        {
            requestedClients.Add(clientName);
            return clientName switch
            {
                "github-oauth" => TextResponse(
                    HttpStatusCode.OK,
                    "access_token=gho_test_token&scope=user%3Aemail&token_type=bearer"),
                "github-api" when request.RequestUri!.AbsolutePath == "/user" => JsonResponse(
                    HttpStatusCode.OK,
                    """{"id":12345,"login":"octocat"}"""),
                "github-api" when request.RequestUri!.AbsolutePath == "/user/emails" => JsonResponse(
                    HttpStatusCode.OK,
                    """
                    [
                      {"email":"secondary@example.com","primary":false,"verified":true},
                      {"email":"octocat@example.com","primary":true,"verified":true}
                    ]
                    """),
                _ => JsonResponse(HttpStatusCode.NotFound, "{}")
            };
        });

        var profile = await client.GetProfileAsync(
            "client-id",
            "client-secret",
            "callback-code",
            "https://api.liveauth.app/api/dev/auth/github/callback",
            CancellationToken.None);

        profile.Should().Be(new GitHubOAuthProfile("12345", "octocat", "octocat@example.com"));
        requestedClients.Should().Equal("github-oauth", "github-api", "github-api");
    }

    [Fact]
    public async Task GetProfileAsync_ReturnsNullWhenTokenExchangeFails()
    {
        var apiRequested = false;
        var client = CreateClient((clientName, _) =>
        {
            apiRequested |= clientName == "github-api";
            return clientName == "github-oauth"
                ? TextResponse(HttpStatusCode.BadRequest, "error=bad_verification_code")
                : JsonResponse(HttpStatusCode.OK, "{}");
        });

        var profile = await client.GetProfileAsync(
            "client-id",
            "client-secret",
            "bad-code",
            "https://api.liveauth.app/api/dev/auth/github/callback",
            CancellationToken.None);

        profile.Should().BeNull();
        apiRequested.Should().BeFalse();
    }

    [Fact]
    public async Task GetProfileAsync_ReturnsProfileWithNullEmailWhenEmailsEndpointFails()
    {
        var client = CreateClient((clientName, request) =>
        {
            return clientName switch
            {
                "github-oauth" => TextResponse(HttpStatusCode.OK, "access_token=gho_test_token"),
                "github-api" when request.RequestUri!.AbsolutePath == "/user" => JsonResponse(
                    HttpStatusCode.OK,
                    """{"id":"github-user-id","login":"github-user"}"""),
                "github-api" when request.RequestUri!.AbsolutePath == "/user/emails" => JsonResponse(
                    HttpStatusCode.Forbidden,
                    "{}"),
                _ => JsonResponse(HttpStatusCode.NotFound, "{}")
            };
        });

        var profile = await client.GetProfileAsync(
            "client-id",
            "client-secret",
            "callback-code",
            "https://api.liveauth.app/api/dev/auth/github/callback",
            CancellationToken.None);

        profile.Should().Be(new GitHubOAuthProfile("github-user-id", "github-user", null));
    }

    private static GitHubOAuthClient CreateClient(
        Func<string, HttpRequestMessage, HttpResponseMessage> responder)
    {
        return new GitHubOAuthClient(new StubHttpClientFactory(responder));
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage TextResponse(HttpStatusCode statusCode, string text)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(text, Encoding.UTF8, "text/plain")
        };
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly Func<string, HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpClientFactory(
            Func<string, HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new StubHttpMessageHandler(request => _responder(name, request)));
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}
