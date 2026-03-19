namespace LiveAuthCore.Data.Entities;

/// <summary>
/// Session for Nostr-based agent authentication
/// </summary>
public class NostrAgentSession
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// Public key in hex format (32 bytes)
    /// </summary>
    public string NpubHex { get; set; } = string.Empty;
    
    /// <summary>
    /// Lightning address for receiving zaps (e.g., agent@getalby.com)
    /// </summary>
    public string? Lud16 { get; set; }
    
    /// <summary>
    /// Challenge string the agent must sign
    /// </summary>
    public string Challenge { get; set; } = string.Empty;
    
    /// <summary>
    /// When the agent successfully verified ownership
    /// </summary>
    public DateTime? VerifiedAt { get; set; }
    
    /// <summary>
    /// When this session expires
    /// </summary>
    public DateTime ExpiresAt { get; set; }
    
    /// <summary>
    /// When this session was created
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
