using System.Net.Http.Json;
using System.Text.Json;

namespace LiveAuthCore.Services;

public class EmailService
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _fromEmail;
    private readonly string _fromName;
    private readonly IConfiguration _config;

    public EmailService(HttpClient http, IConfiguration config)
    {
        _http = http;
        _config = config;
        _apiKey = config["Resend:ApiKey"] ?? throw new InvalidOperationException("Resend:ApiKey is not configured.");
        _fromEmail = config["Resend:FromEmail"] ?? "noreply@liveauth.app";
        _fromName = config["Resend:FromName"] ?? "LiveAuth";
        _http.DefaultRequestHeaders.Clear();
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
    }

    public async Task<bool> SendEmailAsync(string to, string subject, string htmlBody)
    {
        try
        {
            var payload = new
            {
                from = $"{_fromName} <{_fromEmail}>",
                to = new[] { to },
                subject = subject,
                html = htmlBody
            };

            var response = await _http.PostAsJsonAsync("https://api.resend.com/emails", payload);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Email send failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> SendVerificationEmailAsync(string to, string verificationToken)
    {
        var verifyUrl = $"https://liveauth.app/dev/verify-email?token={verificationToken}";
        var html = $@"
<!DOCTYPE html>
<html>
<head>
  <meta charset='utf-8'>
  <style>
    body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; color: #1a1a2e; max-width: 600px; margin: 0 auto; padding: 20px; }}
    .header {{ font-size: 24px; font-weight: 700; margin-bottom: 20px; color: #00C2FF; }}
    .button {{ display: inline-block; background: #00C2FF; color: #000 !important; padding: 12px 24px; text-decoration: none; border-radius: 8px; font-weight: 600; margin: 16px 0; }}
    .footer {{ font-size: 12px; color: #888; margin-top: 32px; border-top: 1px solid #eee; padding-top: 16px; }}
  </style>
</head>
<body>
  <div class='header'>⚡ LiveAuth</div>
  <h2>Verify your email</h2>
  <p>Click the button below to verify your email address and activate your LiveAuth developer account.</p>
  <a href='{verifyUrl}' class='button'>Verify Email Address</a>
  <p>Or copy and paste this link into your browser:</p>
  <p style='word-break: break-all; color: #666;'>{verifyUrl}</p>
  <p>This link expires in 24 hours.</p>
  <div class='footer'>
    LiveAuth — Human verification through economics, not heuristics.<br>
    If you didn't create a LiveAuth account, you can safely ignore this email.
  </div>
</body>
</html>";

        return await SendEmailAsync(to, "Verify your LiveAuth email", html);
    }
}