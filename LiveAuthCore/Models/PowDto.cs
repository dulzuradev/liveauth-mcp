using System.Text.Json.Serialization;

public sealed record PowChallengeResponse
(
    [property: JsonPropertyName("projectPublicKey")]
    string ProjectPublicKey,

    [property: JsonPropertyName("challengeHex")]
    string ChallengeHex,

    [property: JsonPropertyName("targetHex")]
    string TargetHex,

    [property: JsonPropertyName("difficultyBits")]
    int DifficultyBits,

    [property: JsonPropertyName("expiresAtUnix")]
    long ExpiresAtUnix,

    // HMAC / Ed25519 signature over the canonical payload
    [property: JsonPropertyName("sig")]
    string Signature
);

public sealed record PowVerifyRequest
(
    [property: JsonPropertyName("challengeHex")]
    string ChallengeHex,

    [property: JsonPropertyName("nonce")]
    long Nonce,

    [property: JsonPropertyName("hashHex")]
    string HashHex,

    [property: JsonPropertyName("expiresAtUnix")]
    long ExpiresAtUnix,

    [property: JsonPropertyName("difficultyBits")]
    int DifficultyBits,
    
    [property: JsonPropertyName("sig")]
    string Sig
);

public sealed record PowVerifyResponse
(
    [property: JsonPropertyName("verified")]
    bool Verified,

    [property: JsonPropertyName("token")]
    string? Token,

    [property: JsonPropertyName("fallback")]
    string? Fallback
);

public sealed record PowStats(
    double AvgSolveMs,
    int Attempts,
    int Failures
);

public sealed record PowAttemptStats
{
    public long Attempts { get; init; }
    public long Successes { get; init; }
    public long Failures { get; init; }
    public double AvgSolveMs { get; init; }
    public long LastSeenUnix { get; init; }
}

