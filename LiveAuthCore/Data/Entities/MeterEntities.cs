using System.ComponentModel.DataAnnotations;

namespace LiveAuthCore.Data.Entities;

public static class MeterEnvironments
{
    public const string Test = "TEST";
    public const string Live = "LIVE";
}

public static class MeterUnmatchedRouteBehaviors
{
    public const string Free = "FREE";
    public const string Block = "BLOCK";
    public const string DefaultPrice = "DEFAULT_PRICE";
}

public static class MeterChallengeStatuses
{
    public const string Pending = "PENDING";
    public const string Paid = "PAID";
    public const string Exhausted = "EXHAUSTED";
    public const string Expired = "EXPIRED";
}

public sealed class MeterProjectSettings
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public bool Enabled { get; set; }
    [MaxLength(2048)] public string? OriginBaseUrl { get; set; }
    [MaxLength(8)] public string Environment { get; set; } = MeterEnvironments.Test;
    [MaxLength(253)] public string? PublicGatewayHostname { get; set; }
    public int OriginTimeoutSeconds { get; set; } = 30;
    public long MonthlyFreeRequestAllowance { get; set; }
    public long DefaultPriceSats { get; set; } = 1;
    [MaxLength(32)] public string UnmatchedRouteBehavior { get; set; } = MeterUnmatchedRouteBehaviors.Block;
    public bool ReceiptSigningEnabled { get; set; } = true;
    [MaxLength(2048)] public string? WebhookUrl { get; set; }
    public Guid? LightningConnectionId { get; set; }
    public MerchantLightningConnection? LightningConnection { get; set; }
    public bool AllowPrivateOriginInTest { get; set; }
    public long MaximumRequestBodyBytes { get; set; } = 2 * 1024 * 1024;
    public long MaximumResponseBodyBytes { get; set; } = 10 * 1024 * 1024;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class MeterRouteRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    [MaxLength(16)] public string HttpMethod { get; set; } = "GET";
    [MaxLength(1024)] public string PathPattern { get; set; } = "/";
    public long PriceSats { get; set; }
    public long FreeRequestAllowance { get; set; }
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; }
    public int? CredentialLifetimeSeconds { get; set; }
    public int? MaximumCredentialUses { get; set; }
    public bool BindRequestBody { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class MerchantLightningConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    [MaxLength(32)] public string ProviderType { get; set; } = "LND_REST";
    [MaxLength(120)] public string DisplayName { get; set; } = "Merchant LND";
    [MaxLength(2048)] public string RestUrl { get; set; } = string.Empty;
    public string? EncryptedTlsCertificate { get; set; }
    public string EncryptedMacaroon { get; set; } = string.Empty;
    public bool SupportsPaymentLookup { get; set; } = true;
    public DateTime? LastValidatedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class MeterPaymentChallenge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    [MaxLength(8)] public string Environment { get; set; } = MeterEnvironments.Test;
    public Guid? RouteRuleId { get; set; }
    public MeterRouteRule? RouteRule { get; set; }
    [MaxLength(16)] public string HttpMethod { get; set; } = "GET";
    [MaxLength(2048)] public string RequestedPath { get; set; } = "/";
    [MaxLength(1024)] public string NormalizedRoute { get; set; } = "/";
    public long PriceSats { get; set; }
    [MaxLength(128)] public string PaymentHash { get; set; } = string.Empty;
    public string Invoice { get; set; } = string.Empty;
    public Guid MerchantLightningProviderId { get; set; }
    public MerchantLightningConnection MerchantLightningProvider { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime CredentialExpiresAt { get; set; }
    public int MaximumUses { get; set; } = 1;
    public int RemainingUses { get; set; } = 1;
    [MaxLength(32)] public string Status { get; set; } = MeterChallengeStatuses.Pending;
    [MaxLength(128)] public string RequestCorrelationId { get; set; } = string.Empty;
    [MaxLength(128)] public string ChallengeKey { get; set; } = string.Empty;
    [MaxLength(128)] public string CredentialNonce { get; set; } = string.Empty;
    [MaxLength(128)] public string? RequestBodyHash { get; set; }
    public string Macaroon { get; set; } = string.Empty;
}

public sealed class MeterAllowanceCounter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    [MaxLength(8)] public string Environment { get; set; } = MeterEnvironments.Test;
    [MaxLength(7)] public string MonthUtc { get; set; } = string.Empty;
    [MaxLength(128)] public string CallerKey { get; set; } = string.Empty;
    [MaxLength(80)] public string ScopeKey { get; set; } = "project";
    public long Used { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class MeterUsageEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid? RouteRuleId { get; set; }
    public Guid? ChallengeId { get; set; }
    [MaxLength(8)] public string Environment { get; set; } = MeterEnvironments.Test;
    [MaxLength(32)] public string Kind { get; set; } = string.Empty;
    [MaxLength(16)] public string HttpMethod { get; set; } = string.Empty;
    [MaxLength(2048)] public string Path { get; set; } = string.Empty;
    [MaxLength(1024)] public string NormalizedRoute { get; set; } = string.Empty;
    public long AmountSats { get; set; }
    public int? OriginStatusCode { get; set; }
    public long GatewayLatencyMilliseconds { get; set; }
    public long? OriginLatencyMilliseconds { get; set; }
    [MaxLength(128)] public string CorrelationId { get; set; } = string.Empty;
    [MaxLength(128)] public string CallerKey { get; set; } = string.Empty;
    [MaxLength(128)] public string? ErrorCode { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class MeterReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid ChallengeId { get; set; }
    [MaxLength(128)] public string RequestCorrelationId { get; set; } = string.Empty;
    [MaxLength(32)] public string Version { get; set; } = "meter-receipt-v1";
    public string CanonicalPayload { get; set; } = string.Empty;
    [MaxLength(256)] public string Signature { get; set; } = string.Empty;
    [MaxLength(32)] public string SignatureAlgorithm { get; set; } = "HMAC-SHA256";
    [MaxLength(128)] public string KeyId { get; set; } = "liveauth-meter-v1";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
