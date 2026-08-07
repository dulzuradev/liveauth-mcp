namespace LiveAuthCore.Models.Meter;

public sealed record MeterSettingsDto(
    bool Enabled, string? OriginBaseUrl, string Environment, string? PublicGatewayHostname,
    int OriginTimeoutSeconds, long MonthlyFreeRequestAllowance, long DefaultPriceSats,
    string UnmatchedRouteBehavior, bool ReceiptSigningEnabled, string? WebhookUrl,
    bool AllowPrivateOriginInTest, long MaximumRequestBodyBytes, long MaximumResponseBodyBytes,
    MeterLightningConnectionDto? LightningConnection);

public sealed record UpdateMeterSettingsRequest(
    bool Enabled, string? OriginBaseUrl, string Environment, string? PublicGatewayHostname,
    int OriginTimeoutSeconds, long MonthlyFreeRequestAllowance, long DefaultPriceSats,
    string UnmatchedRouteBehavior, bool ReceiptSigningEnabled, string? WebhookUrl,
    bool AllowPrivateOriginInTest, long MaximumRequestBodyBytes = 2_097_152,
    long MaximumResponseBodyBytes = 10_485_760);

public sealed record MeterRouteRuleDto(Guid Id, string HttpMethod, string PathPattern, long PriceSats,
    long FreeRequestAllowance, bool Enabled, int Priority, int? CredentialLifetimeSeconds,
    int? MaximumCredentialUses, bool BindRequestBody, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record UpsertMeterRouteRuleRequest(string HttpMethod, string PathPattern, long PriceSats,
    long FreeRequestAllowance, bool Enabled, int Priority, int? CredentialLifetimeSeconds,
    int? MaximumCredentialUses, bool BindRequestBody = false);

public sealed record MeterLightningConnectionDto(Guid Id, string ProviderType, string DisplayName,
    string RestUrl, bool HasTlsCertificate, bool HasMacaroon, bool SupportsPaymentLookup,
    DateTime? LastValidatedAt);

public sealed record UpsertMeterLightningConnectionRequest(string ProviderType, string DisplayName,
    string RestUrl, string? TlsCertificate, string? Macaroon, bool SupportsPaymentLookup = true);

public sealed record MeterLightningTestResponse(bool Success, string? Alias, string? Version, string? Error);

public sealed record MeterReceiptDto(Guid Id, Guid ChallengeId, string RequestCorrelationId, string Version,
    string CanonicalPayload, string Signature, string SignatureAlgorithm, string KeyId,
    bool SignatureValid, DateTime CreatedAt);

public sealed record MeterRouteAnalyticsDto(string Route, long Requests, long PaidRequests, long RevenueSats);
public sealed record MeterRecentPaidRequestDto(DateTime CreatedAt, string Method, string Route,
    long AmountSats, int? OriginStatusCode, string CorrelationId, Guid? ChallengeId);
public sealed record MeterAnalyticsDto(
    DateTime WindowStart, DateTime WindowEnd, long TotalGatewayRequests, long FreeRequests,
    long PaidRequests, long PaymentChallengesIssued, double PaymentConversionRate,
    long RevenueSats, double AverageSatsPerPaidRequest, double GatewayErrorRate,
    double OriginErrorRate, double AverageLatencyMilliseconds,
    IReadOnlyList<MeterRouteAnalyticsDto> TopRoutes,
    IReadOnlyList<MeterRecentPaidRequestDto> RecentPaidRequests);
