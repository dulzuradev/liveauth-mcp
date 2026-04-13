namespace LiveAuthCore.Models;

public sealed class ListProjectsResponse
{
    public IReadOnlyList<ProjectDto> Projects { get; set; } = Array.Empty<ProjectDto>();
}

public sealed class ProjectDto
{
    public required Guid ProjectId { get; set; }
    public required string Name { get; set; }
    public required string PublicKey { get; set; }
    public required string Plan { get; set; }
    public long MonthlyQuota { get; set; }
    public long MonthlyUsed { get; set; }
    public required DateTime CreatedAt { get; set; }
    
    public string Environment { get; set; } = "TEST";  // 👈 NEW
    public bool Active { get; set; }                   // 👈 NEW (maps IsActive)
    
    public int SatsPerLogin { get; set; }
    
    public DateTime? ProPaidUntil { get; set; } 
    
    public DateTime MonthlyAuthPeriodStart { get; set; }

    public long L402BalanceSats { get; set; }
    
}

public sealed class ProjectApiKeyDto
{
    public Guid Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public bool IsActive { get; set; }
}

public sealed class ListProjectApiKeysResponse
{
    public IReadOnlyList<ProjectApiKeyDto> Keys { get; set; } = Array.Empty<ProjectApiKeyDto>();
}

public sealed class CreateApiKeyRequest
{
    public string Label { get; set; } = string.Empty;
}

public sealed class CreateApiKeyResponse
{
    public Guid Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty; // shown once
}

public sealed class UpdateApiKeyLabelRequest
{
    public string Label { get; set; } = string.Empty;
}

public class UpdateProjectEnvironmentRequest
{
    public string Environment { get; set; } = "TEST"; // "TEST" or "LIVE"
}
