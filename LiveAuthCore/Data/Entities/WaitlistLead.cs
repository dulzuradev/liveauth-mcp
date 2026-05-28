using System.ComponentModel.DataAnnotations;

namespace LiveAuthCore.Data.Entities;

public class WaitlistLead
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(320)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string UseCase { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? GithubOrTwitter { get; set; }

    [MaxLength(100)]
    public string Source { get; set; } = "liveauth.app";

    [MaxLength(500)]
    public string? UserAgent { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
