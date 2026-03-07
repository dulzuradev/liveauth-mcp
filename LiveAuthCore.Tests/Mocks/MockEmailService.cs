namespace LiveAuthCore.Tests.Mocks;

/// <summary>
/// Mock email service for testing - just logs instead of sending
/// </summary>
public class MockEmailService
{
    public Task SendEmailAsync(string to, string subject, string body)
    {
        // No-op in tests - just log
        Console.WriteLine($"[MOCK EMAIL] To: {to}, Subject: {subject}");
        return Task.CompletedTask;
    }

    public Task SendPasswordResetEmailAsync(string email, string resetToken)
    {
        Console.WriteLine($"[MOCK EMAIL] Password reset for {email}");
        return Task.CompletedTask;
    }
}
