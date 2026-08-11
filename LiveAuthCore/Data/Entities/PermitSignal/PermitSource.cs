using System.ComponentModel.DataAnnotations;

namespace LiveAuthCore.Data.Entities.PermitSignal;

public sealed class PermitSource
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(80)]
    public string SourceIdentifier { get; set; } = string.Empty;

    [MaxLength(120)]
    public string Municipality { get; set; } = string.Empty;

    [MaxLength(2)]
    public string State { get; set; } = string.Empty;

    [MaxLength(120)]
    public string AdapterType { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string OfficialDatasetUrl { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    [MaxLength(32)]
    public string HealthStatus { get; set; } = "Pending";

    public DateTime? LastSuccessfulSync { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public PermitSyncState? SyncState { get; set; }
    public ICollection<PermitProject> Projects { get; set; } = new List<PermitProject>();
}

public sealed class PermitSyncState
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PermitSourceId { get; set; }
    public PermitSource PermitSource { get; set; } = null!;

    public DateTime? LastAttemptAt { get; set; }
    public DateTime? LastSuccessfulSyncAt { get; set; }
    public DateTime? SourceCursorUtc { get; set; }

    [MaxLength(500)]
    public string? ContinuationToken { get; set; }

    public int ConsecutiveFailures { get; set; }
    public long RecordsProcessed { get; set; }
    public string? LastError { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
