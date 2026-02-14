namespace LiveAuthCore.Services;

// NUT-04: Minting
public class MintQuoteRequest
{
    public long Amount { get; set; }
    public string Unit { get; set; } = "sat";
}

public class MintQuoteResponse
{
    public string Quote { get; set; } = string.Empty;
    public string Request { get; set; } = string.Empty; // bolt11 invoice
    public bool Paid { get; set; }
    public long Expiry { get; set; }
}

public class MintQuoteStatusResponse
{
    public string Quote { get; set; } = string.Empty;
    public string Request { get; set; } = string.Empty;
    public bool Paid { get; set; }
    public string State { get; set; } = string.Empty; // UNPAID, PAID, ISSUED
    public long Expiry { get; set; }
}

public class MintBolt11Request
{
    public string Quote { get; set; } = string.Empty;
    public List<BlindedMessage> Outputs { get; set; } = new();
}

public class MintBolt11Response
{
    public List<BlindedSignature> Signatures { get; set; } = new();
}

// NUT-05: Melting
public class MeltQuoteRequest
{
    public string Request { get; set; } = string.Empty; // bolt11 invoice
    public string Unit { get; set; } = "sat";
}

public class MeltQuoteResponse
{
    public string Quote { get; set; } = string.Empty;
    public long Amount { get; set; }
    public long FeeReserve { get; set; }
    public bool Paid { get; set; }
    public long Expiry { get; set; }
}

public class MeltBolt11Request
{
    public string Quote { get; set; } = string.Empty;
    public List<CashuProof> Inputs { get; set; } = new();
}

public class MeltBolt11Response
{
    public bool Paid { get; set; }
    public string? Payment_preimage { get; set; }
    public List<BlindedSignature>? Change { get; set; }
}

// Keyset management
public class KeysetsResponse
{
    public List<KeysetInfo> Keysets { get; set; } = new();
}

public class KeysetInfo
{
    public string Id { get; set; } = string.Empty;
    public string Unit { get; set; } = "sat";
    public bool Active { get; set; }
}

public class KeysResponse
{
    public Dictionary<string, string> Keysets { get; set; } = new(); // amount -> pubkey (hex)
}
