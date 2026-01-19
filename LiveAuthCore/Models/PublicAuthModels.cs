namespace LiveAuthCore.Models;

public sealed class PublicStartAuthRequest
{
    /// <summary>
    /// Optional user hint (email, username, etc.) the integrator can pass.
    /// </summary>
    public string? UserHint { get; set; }
}

public sealed class PublicStartAuthResponse
{
    public Guid SessionId { get; set; }
    public string? Invoice { get; set; }        // BOLT11, null in TEST
    public long AmountSats { get; set; }
    public long ExpiresAtUnix { get; set; }
    public string Mode { get; set; } = "TEST";  // "TEST" | "LIVE"
}

public sealed class PublicConfirmAuthRequest
{
    public Guid SessionId { get; set; }
    
    public bool Simulate { get; set; } = false;
}

public sealed class PublicConfirmAuthResponse
{
    public bool Verified { get; set; }
    public string? Token { get; set; }
}

public class PublicAuthStartRequest
{
    public string ProjectPublicKey { get; set; } = string.Empty;

    // Optional metadata / hints:
    public string? Context { get; set; }        // e.g. "login", "comment", "vote"
    public string? RedirectUrl { get; set; }    // page where user is
    public bool? DemoMode { get; set; }         // allow demo/simulated auth for your public demo
}

public class PublicAuthStartResponse
{
    public Guid SessionId { get; set; }

    // If payment is required (LIVE + satsPerLogin > 0)
    public bool PaymentRequired { get; set; }

    // Only populated when PaymentRequired = true
    public string? Invoice { get; set; }        // BOLT11
    public long AmountSats { get; set; }
    public long ExpiresAtUnix { get; set; }

    // For UI:
    public string Mode { get; set; } = "TEST";  // "TEST" | "LIVE"
}

public class PublicAuthConfirmRequest
{
    public Guid SessionId { get; set; }
}

public class PublicAuthConfirmResponse
{
    public bool Verified { get; set; }

    // JWT or signed proof you issue for this **end-user session**
    public string? Token { get; set; }

    // For UI/debugging
    public string? Mode { get; set; }          // "TEST" | "LIVE"
    public bool? PaymentRequired { get; set; } // mirrors start
}
