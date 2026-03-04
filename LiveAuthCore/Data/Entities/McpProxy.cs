using System.ComponentModel.DataAnnotations;

namespace LiveAuthCore.Data.Entities;

/// <summary>
/// Registered MCP server proxy configuration
/// </summary>
public class McpProxy
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }
    
    /// <summary>
    /// User-friendly name for this proxy
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = "";
    
    /// <summary>
    /// Upstream MCP server URL (e.g., https://mcp-server.example.com)
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string UpstreamUrl { get; set; } = "";
    
    /// <summary>
    /// Price per request in sats
    /// </summary>
    public int SatsPerRequest { get; set; } = 1;
    
    /// <summary>
    /// Whether this proxy is active
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// Custom path for this proxy (e.g., "my-agent" -> /mcp/my-agent)
    /// </summary>
    [MaxLength(50)]
    public string? CustomPath { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Total requests served
    /// </summary>
    public long TotalRequests { get; set; }
    
    /// <summary>
    /// Total sats earned
    /// </summary>
    public long TotalSatsEarned { get; set; }
}
