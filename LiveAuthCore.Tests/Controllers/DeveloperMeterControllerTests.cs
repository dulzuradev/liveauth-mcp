using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace LiveAuthCore.Tests.Controllers;

public sealed class DeveloperMeterControllerTests : IClassFixture<LiveAuthWebApplicationFactory>
{
    private static readonly Guid OwnerId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid ProjectId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private readonly HttpClient _client;
    public DeveloperMeterControllerTests(LiveAuthWebApplicationFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Project_owner_can_read_settings_but_another_developer_cannot()
    {
        var own = new HttpRequestMessage(HttpMethod.Get, $"/api/dev/projects/{ProjectId}/meter");
        own.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Jwt(OwnerId));
        (await _client.SendAsync(own)).StatusCode.Should().Be(HttpStatusCode.OK);

        var other = new HttpRequestMessage(HttpMethod.Get, $"/api/dev/projects/{ProjectId}/meter");
        other.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Jwt(Guid.NewGuid()));
        (await _client.SendAsync(other)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Saved_lightning_response_never_returns_macaroon_contents()
    {
        var secret = "0201036c6e6402invoice-macaroon-secret";
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/dev/projects/{ProjectId}/meter/lightning")
        {
            Content = JsonContent.Create(new
            {
                providerType = "LND_REST", displayName = "Test merchant LND",
                restUrl = "https://lnd.example.test:8080", macaroon = secret,
                tlsCertificate = (string?)null, supportsPaymentLookup = true
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Jwt(OwnerId));
        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotContain(secret);
        json.ToLowerInvariant().Should().NotContain("encryptedmacaroon");
        json.Should().Contain("\"hasMacaroon\":true");
    }

    private static string Jwt(Guid developerId)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-jwt-signing-key-that-is-at-least-32-bytes-long")),
            SecurityAlgorithms.HmacSha256);
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            claims: new[] { new Claim("userId", developerId.ToString()) },
            expires: DateTime.UtcNow.AddMinutes(10), signingCredentials: credentials));
    }
}
