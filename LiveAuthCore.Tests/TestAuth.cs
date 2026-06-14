using System.Security.Cryptography;
using System.Text;
using LiveAuthCore.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LiveAuthCore.Tests;

internal static class TestAuth
{
    public static string GenerateAdminJwt(LiveAuthWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var lightning = scope.ServiceProvider.GetRequiredService<LightningService>();
        return lightning.GenerateAdminJwtToken(Guid.NewGuid().ToString());
    }

    public static string GenerateDeveloperJwt(LiveAuthWebApplicationFactory factory, Guid developerId)
    {
        using var scope = factory.Services.CreateScope();
        var lightning = scope.ServiceProvider.GetRequiredService<LightningService>();
        return lightning.GenerateDeveloperJwtToken(developerId.ToString());
    }

    public static (string Hash, string Salt) HashPasswordWithSalt(string password)
    {
        var saltBytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(saltBytes);

        var salt = Convert.ToBase64String(saltBytes);
        return (HashPassword(password, salt), salt);
    }

    private static string HashPassword(string password, string salt)
    {
        var saltBytes = Convert.FromBase64String(salt);
        using var pbkdf2 = new Rfc2898DeriveBytes(password, saltBytes, 100_000, HashAlgorithmName.SHA256);
        return Convert.ToBase64String(pbkdf2.GetBytes(32));
    }
}
