using LiveAuthCore.Entities;

namespace LiveAuthCore.Data.Entities;

public class Developer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? LightningAuthKey { get; set; }
    public string? GitHubId { get; set; }      // GitHub OAuth user ID
    public string? GitHubUsername { get; set; }  // GitHub username
    
    // Email/password auth
    public string? PasswordHash { get; set; }
    public string? PasswordSalt { get; set; }
    
    // Email verification
    public bool EmailVerified { get; set; } = false;
    public string? VerificationToken { get; set; }
    public DateTime? VerificationExpiresAt { get; set; }
    
    public List<Project> Projects { get; set; } = new();
}