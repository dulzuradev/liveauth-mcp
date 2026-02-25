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
    public List<Project> Projects { get; set; } = new();
}