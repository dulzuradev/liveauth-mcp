namespace LiveAuthCore.Models;

public class GitHubLoginStatusResponse
{
    public bool Enabled { get; set; }
}

public class GitHubLoginResponse
{
    public string Token { get; set; } = string.Empty;
    public DeveloperInfo Developer { get; set; } = new();
}

public class DeveloperInfo
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? GitHubUsername { get; set; }
}
