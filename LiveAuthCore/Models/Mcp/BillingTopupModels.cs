namespace LiveAuthCore.Models.Mcp;

public record TopupRequest(
    /// <summary>
    /// Optional. If omitted, tops up the developer's default project.
    /// </summary>
    Guid? ProjectId,

    /// <summary>
    /// Sats to add to L402 balance. Max 1,000,000 per call.
    /// </summary>
    long AmountSats
);

public record TopupResponse(
    Guid ProjectId,
    long AmountAdded,
    long NewBalance
);
