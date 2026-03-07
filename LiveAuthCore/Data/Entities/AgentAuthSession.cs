using System.ComponentModel.DataAnnotations;

namespace LiveAuthCore.Data.Entities;

/// <summary>
/// Agent authentication session (for AI agents like OpenClaw)
/// </summary>
public class AgentAuthSession
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public string AgentId { get; set; } = string.Empty;

    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }

    [Required]
    public string Challenge { get; set; } = string.Empty;

    public int DifficultyBits { get; set; }

    public string? Solution { get; set; }

    public bool IsVerified { get; set; }

    public string? AuthToken { get; set; }

    public DateTime? SolvedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
