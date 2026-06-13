using System.ComponentModel.DataAnnotations;

namespace LiveAuthCore.Data.Entities;

/// <summary>
/// L402 Bundle purchase — a prepaid block of MCP/agent calls.
/// </summary>
public class L402Bundle
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Human-readable bundle ID, e.g. "bundle_growth_abc123"
    /// </summary>
    public string BundleId { get; set; } = string.Empty;

    /// <summary>
    /// Project this bundle belongs to.
    /// </summary>
    public Guid ProjectId { get; set; }

    /// <summary>
    /// Developer who purchased this bundle.
    /// </summary>
    public Guid DeveloperId { get; set; }

    /// <summary>
    /// Bundle tier name: starter, growth, scale, enterprise
    /// </summary>
    public string Tier { get; set; } = string.Empty;

    /// <summary>
    /// Total calls included in this bundle.
    /// </summary>
    public int TotalCalls { get; set; }

    /// <summary>
    /// Calls remaining. Decremented on each MCP call.
    /// </summary>
    public int RemainingCalls { get; set; }

    /// <summary>
    /// Unix timestamp when bundle expires.
    /// </summary>
    public long ExpiresAtUnix { get; set; }

    /// <summary>
    /// When the bundle was purchased/activated.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Lightning payment hash (r_hash hex) for this bundle's invoice.
    /// </summary>
    public string PaymentHash { get; set; } = string.Empty;

    /// <summary>
    /// Bolt11 invoice string.
    /// </summary>
    public string Bolt11 { get; set; } = string.Empty;

    /// <summary>
    /// Amount paid in sats.
    /// </summary>
    public long AmountSats { get; set; }

    public long BaseAmountSats { get; set; }
    public int MarkupBasisPoints { get; set; }
    public long MarkupMinimumFeeSats { get; set; }
    public long MarkupSats { get; set; }
    public long TotalChargedSats { get; set; }
    public long CreditAmountSats { get; set; }

    /// <summary>
    /// pending → paid → active → expired | depleted
    /// </summary>
    public string Status { get; set; } = "pending";

    /// <summary>
    /// Optional agent ID bound to this bundle.
    /// </summary>
    public string? AgentId { get; set; }

    // Navigation
    public Project? Project { get; set; }
}

/// <summary>
/// Macaroon credential issued for an L402 bundle.
/// </summary>
public class L402Macaroon
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Unique token ID (jti) — used for revocation.
    /// </summary>
    public string Jti { get; set; } = string.Empty;

    /// <summary>
    /// Bundle this macaroon is issued from.
    /// </summary>
    public Guid BundleId { get; set; }

    /// <summary>
    /// Project this macaroon grants access to.
    /// </summary>
    public Guid ProjectId { get; set; }

    /// <summary>
    /// Optional agent ID bound to this macaroon.
    /// </summary>
    public string? AgentId { get; set; }

    /// <summary>
    /// Scopes granted by this macaroon (e.g. ["mcp.verify", "auth.start"])
    /// </summary>
    public string ScopesJson { get; set; } = "[\"mcp.verify\",\"auth.start\"]";

    /// <summary>
    /// Unix timestamp when this macaroon expires.
    /// </summary>
    public long ExpiresAtUnix { get; set; }

    /// <summary>
    /// When the macaroon was issued.
    /// </summary>
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether this macaroon has been revoked.
    /// </summary>
    public bool IsRevoked { get; set; } = false;

    /// <summary>
    /// HMAC-SHA256 signature over the macaroon claims.
    /// First 16 bytes = key hint, remaining 16 bytes = signature.
    /// </summary>
    public string SignatureB64 { get; set; } = string.Empty;

    // Navigation
    public L402Bundle? Bundle { get; set; }
}
