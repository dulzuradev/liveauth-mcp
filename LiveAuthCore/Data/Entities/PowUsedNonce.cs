namespace LiveAuthCore.Data.Entities;

/// <summary>
/// Tracks used PoW nonces to prevent replay attacks.
/// The unique constraint on (ProjectId, ChallengeHex, Nonce) provides atomic replay protection.
/// </summary>
public class PowUsedNonce
{
    public long Id { get; set; }
    
    public Guid ProjectId { get; set; }
    
    public string ChallengeHex { get; set; } = string.Empty;
    
    public string Nonce { get; set; } = string.Empty;
    
    public DateTime UsedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime ExpiresAt { get; set; }
}
