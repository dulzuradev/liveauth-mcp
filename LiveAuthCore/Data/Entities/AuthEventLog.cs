using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace LiveAuthCore.Data.Entities;

public class AuthEventLog
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public DateTime OccurredAtUtc { get; set; }

    [Required]
    public string EventType { get; set; } = string.Empty;

    public Guid? ProjectId { get; set; }

    [ForeignKey(nameof(ProjectId))]
    public Project? Project { get; set; }

    [Required]
    public string RequestId { get; set; } = string.Empty;

    public string? IpMasked { get; set; }

    public int? Sats { get; set; }

    public string? Reason { get; set; }

    public string? Metadata { get; set; } // JSON string
}
