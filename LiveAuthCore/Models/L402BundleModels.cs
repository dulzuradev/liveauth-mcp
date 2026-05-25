namespace LiveAuthCore.Models;

// Bundle tiers and pricing
public static class L402BundleTiers
{
    public const int StarterCalls = 100;
    public const int StarterSats = 50;         // 0.5 sat/call
    public const int GrowthCalls = 1_000;
    public const int GrowthSats = 400;        // 0.4 sat/call
    public const int ScaleCalls = 10_000;
    public const int ScaleSats = 3_000;        // 0.3 sat/call
    public const int EnterpriseCalls = 100_000;
    public const int EnterpriseSats = 20_000;   // 0.2 sat/call

    // Validity period
    public const int DefaultValidityDays = 90;

    public static readonly Dictionary<string, BundleTierConfig> Tiers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["starter"] = new("starter", StarterCalls, StarterSats),
        ["growth"] = new("growth", GrowthCalls, GrowthSats),
        ["scale"] = new("scale", ScaleCalls, ScaleSats),
        ["enterprise"] = new("enterprise", EnterpriseCalls, EnterpriseSats)
    };

    public static bool TryGetTier(string tierName, out BundleTierConfig tier)
        => Tiers.TryGetValue(tierName, out tier);
}

public record BundleTierConfig(
    string Name,
    int TotalCalls,
    int PriceSats
)
{
    public decimal EffectiveRate => (decimal)PriceSats / TotalCalls;
}

// ─── Bundle Purchase ───────────────────────────────────────────

public class CreateBundleInvoiceRequest
{
    public string Tier { get; set; } = "starter";
    public string? AgentId { get; set; }  // Optional agent identifier
    public string? PublicKey { get; set; } // Optional body fallback for X-LW-Public
}

public class CreateBundleInvoiceResponse
{
    public string BundleId { get; set; } = string.Empty;
    public string Invoice { get; set; } = string.Empty;
    public string Bolt11 { get; set; } = string.Empty;
    public string PaymentHash { get; set; } = string.Empty;
    public int AmountSats { get; set; }
    public long ExpiresAtUnix { get; set; }
    public string Tier { get; set; } = string.Empty;
    public int TotalCalls { get; set; }
}

// ─── Bundle Claim ──────────────────────────────────────────────

public class ClaimBundleRequest
{
    public string PaymentHash { get; set; } = string.Empty;
}

public class ClaimBundleResponse
{
    public string Macaroon { get; set; } = string.Empty;
    public string BundleId { get; set; } = string.Empty;
    public int RemainingCalls { get; set; }
    public long ExpiresAtUnix { get; set; }
    public string[] Scopes { get; set; } = Array.Empty<string>();
}

// ─── Bundle Status ────────────────────────────────────────────

public class BundleStatusRequest
{
    public string BundleId { get; set; } = string.Empty;
}

public class BundleStatusResponse
{
    public string BundleId { get; set; } = string.Empty;
    public string Tier { get; set; } = string.Empty;
    public int TotalCalls { get; set; }
    public int RemainingCalls { get; set; }
    public int UsedCalls { get; set; }
    public long ExpiresAtUnix { get; set; }
    public bool IsExpired { get; set; }
    public bool IsDepleted { get; set; }
}
