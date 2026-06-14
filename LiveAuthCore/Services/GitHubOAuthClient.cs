using System.Globalization;
using System.Text.Json;

namespace LiveAuthCore.Services;

public interface IGitHubOAuthClient
{
    Task<GitHubOAuthProfile?> GetProfileAsync(
        string clientId,
        string clientSecret,
        string code,
        string redirectUri,
        CancellationToken ct);
}

public sealed record GitHubOAuthProfile(
    string Id,
    string Login,
    string? Email);

public class GitHubOAuthClient : IGitHubOAuthClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public GitHubOAuthClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<GitHubOAuthProfile?> GetProfileAsync(
        string clientId,
        string clientSecret,
        string code,
        string redirectUri,
        CancellationToken ct)
    {
        var accessToken = await ExchangeCodeForAccessTokenAsync(
            clientId,
            clientSecret,
            code,
            redirectUri,
            ct);

        if (string.IsNullOrWhiteSpace(accessToken))
            return null;

        var user = await GetUserAsync(accessToken, ct);
        if (user == null)
            return null;

        var email = await GetPrimaryVerifiedEmailAsync(accessToken, ct);
        return new GitHubOAuthProfile(user.Value.Id, user.Value.Login, email);
    }

    private async Task<string?> ExchangeCodeForAccessTokenAsync(
        string clientId,
        string clientSecret,
        string code,
        string redirectUri,
        CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("github-oauth");
        var response = await client.PostAsync(
            "https://github.com/login/oauth/access_token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["code"] = code,
                ["redirect_uri"] = redirectUri
            }),
            ct);

        if (!response.IsSuccessStatusCode)
            return null;

        var responseContent = await response.Content.ReadAsStringAsync(ct);
        var parsed = System.Web.HttpUtility.ParseQueryString(responseContent);
        return parsed["access_token"];
    }

    private async Task<(string Id, string Login)?> GetUserAsync(
        string accessToken,
        CancellationToken ct)
    {
        using var response = await SendGitHubApiRequestAsync(
            HttpMethod.Get,
            "https://api.github.com/user",
            accessToken,
            ct);

        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        if (!root.TryGetProperty("id", out var idElement) ||
            !root.TryGetProperty("login", out var loginElement))
        {
            return null;
        }

        var id = idElement.ValueKind == JsonValueKind.Number
            ? idElement.GetInt64().ToString(CultureInfo.InvariantCulture)
            : idElement.GetString();
        var login = loginElement.GetString();

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(login))
            return null;

        return (id, login);
    }

    private async Task<string?> GetPrimaryVerifiedEmailAsync(
        string accessToken,
        CancellationToken ct)
    {
        try
        {
            using var response = await SendGitHubApiRequestAsync(
                HttpMethod.Get,
                "https://api.github.com/user/emails",
                accessToken,
                ct);

            if (!response.IsSuccessStatusCode)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            foreach (var email in doc.RootElement.EnumerateArray())
            {
                if (email.TryGetProperty("primary", out var primary) &&
                    email.TryGetProperty("verified", out var verified) &&
                    primary.GetBoolean() &&
                    verified.GetBoolean() &&
                    email.TryGetProperty("email", out var emailValue))
                {
                    return emailValue.GetString();
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private async Task<HttpResponseMessage> SendGitHubApiRequestAsync(
        HttpMethod method,
        string url,
        string accessToken,
        CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("github-api");
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Add("Authorization", $"Bearer {accessToken}");
        request.Headers.Add("User-Agent", "LiveAuth");
        return await client.SendAsync(request, ct);
    }
}
