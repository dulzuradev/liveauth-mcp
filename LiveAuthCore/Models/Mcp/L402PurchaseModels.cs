namespace LiveAuthCore.Models.Mcp;

public record PurchaseRequest(
    /// <summary>
    /// Amount of sats to add to L402 balance. Min 10, max 100,000.
    /// </summary>
    long AmountSats,

    /// <summary>
    /// Optional project ID. Defaults to the developer's active project.
    /// </summary>
    Guid? ProjectId = null
);

public record PurchaseResponse(
    Guid PurchaseId,
    string Bolt11,
    long AmountSats,
    long ExpiresAtUnix,
    string Status
);

public record PurchaseStatusResponse(
    Guid PurchaseId,
    string Status,
    long AmountSats,
    long? NewBalanceSats,   // populated after settlement
    string? Bolt11
);
