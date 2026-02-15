using System.ComponentModel.DataAnnotations;

namespace LiveAuthCore.Data.Entities;

public class MintProvider
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string MintUrl { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public DateTime AddedAt { get; set; }
}
