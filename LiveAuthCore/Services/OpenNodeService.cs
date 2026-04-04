namespace LiveAuthCore.Services
{
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text.Json;
    using System.Text;
    public class OpenNodeService
    {
        private readonly HttpClient _httpClient;
        public OpenNodeService(IConfiguration configuration)
        {
            var baseUrl = configuration["OpenNode:BaseUrl"] ?? throw new InvalidOperationException("OpenNode:BaseUrl not configured");
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(baseUrl)
            };
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", configuration["OpenNode:ApiKey"]);
        }
        public async Task<string?> CreateChargeAsync(decimal amount, string currency, string callbackUrl)
        {
            var payload = new
            {
                amount = amount,
                currency = currency,
                callback_url = callbackUrl
            };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("/charges", content);
            response.EnsureSuccessStatusCode();
            var responseBody = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JsonElement>(responseBody);
            return result.GetProperty("data").GetProperty("id").GetString();
        }
    }

}