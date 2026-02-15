using System.ComponentModel.DataAnnotations;

namespace LiveAuthCore.Data.Entities;

public class UserEcashBalance
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required]
    public string MintUrl { get; set; } = string.Empty;

    public long Balance { get; set; }

    public DateTime LastUpdated { get; set; }
}
