namespace LiveAuthCore.Models.CostShield;

public sealed class CreateCostShieldChallengeRequest
{
    public string? ProjectPublicKey { get; set; }
    public string Environment { get; set; } = "TEST";
    public string Action { get; set; } = string.Empty;
    public string? Origin { get; set; }
    public string? Subject { get; set; }
    public string? RiskHint { get; set; }
    public Dictionary<string, string>? ClientMetadata { get; set; }
}

public sealed record CostShieldChallengeResponse(
    string ChallengeId,
    string ProjectPublicKey,
    string Environment,
    string Action,
    Guid ProtectedActionId,
    string TargetHex,
    int DifficultyBits,
    string DifficultyReason,
    long ExpiresAtUnix,
    int ConfigurationVersion,
    string Signature);

public sealed class CompleteCostShieldChallengeRequest
{
    public string? ProjectPublicKey { get; set; }
    public string Environment { get; set; } = "TEST";
    public string Action { get; set; } = string.Empty;
    public string? Origin { get; set; }
    public string? Subject { get; set; }
    public long Nonce { get; set; }
    public int DifficultyBits { get; set; }
    public long ExpiresAtUnix { get; set; }
    public int ConfigurationVersion { get; set; }
    public string Signature { get; set; } = string.Empty;
}

public sealed record CostShieldAuthorizationResponse(
    string Token,
    string TokenType,
    long ExpiresAtUnix,
    Guid AuthorizationId,
    string Action,
    string Environment,
    bool RequireSingleUse);

public sealed class VerifyCostShieldAuthorizationRequest
{
    public string Token { get; set; } = string.Empty;
    public string? Action { get; set; }
    public string? Environment { get; set; }
    public string? Origin { get; set; }
}

public sealed record VerifyCostShieldAuthorizationResponse(
    bool Verified,
    bool Consumed,
    Guid AuthorizationId,
    string Action,
    string Environment,
    string? Origin,
    string VerificationMethod,
    long ExpiresAtUnix,
    bool RequireSingleUse);

public sealed record CostShieldJwksResponse(
    IReadOnlyList<CostShieldJwk> Keys);

public sealed record CostShieldJwk(
    string Kty,
    string Use,
    string Kid,
    string Alg,
    string N,
    string E);
