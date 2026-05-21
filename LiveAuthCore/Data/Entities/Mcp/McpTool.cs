using System.ComponentModel.DataAnnotations;

namespace LiveAuthCore.Data.Entities.Mcp;

public class McpTool
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? DeveloperId { get; set; }

    public Guid? ProjectId { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Description { get; set; } = string.Empty;

    public string? Category { get; set; }

    public string? IconUrl { get; set; }

    public string? WebsiteUrl { get; set; }

    public string? DocsUrl { get; set; }

    public string? ManifestJson { get; set; }

    [MaxLength(50)]
    public string Status { get; set; } = "Draft";

    [MaxLength(50)]
    public string Visibility { get; set; } = "Private";

    public int DefaultCostSats { get; set; } = 1;

    public int MinCostSats { get; set; } = 1;

    public int MaxCostSats { get; set; }

    public string? WebhookUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? RemovedAt { get; set; }
}
