using System.ComponentModel.DataAnnotations;

namespace LiveAuthCore.Data.Entities;

/// <summary>
/// Represents a Cashu ecash proof owned by a user
/// </summary>
public class EcashProof
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required]
    public string MintUrl { get; set; } = string.Empty;

    /// <summary>
    /// Amount in satoshis (power of 2)
    /// </summary>
    public long Amount { get; set; }

    /// <summary>
    /// The keyset ID from the mint
    /// </summary>
    [Required]
    public string KeysetId { get; set; } = string.Empty;

    /// <summary>
    /// The secret (hex-encoded)
    /// </summary>
    [Required]
    public string Secret { get; set; } = string.Empty;

    /// <summary>
    /// The unblinded signature point C (hex-encoded)
    /// </summary>
    [Required]
    public string C { get; set; } = string.Empty;

    /// <summary>
    /// Whether this proof has been spent
    /// </summary>
    public bool IsSpent { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? SpentAt { get; set; }

    /// <summary>
    /// Optional: Reference to the mint request that created this proof
    /// </summary>
    public Guid? MintRequestId { get; set; }
}
