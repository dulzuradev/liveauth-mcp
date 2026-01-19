namespace LiveAuthCore.Models;

public class PublicStartLoginRequest
{
    public string? UserHint { get; set; }  // optional; maybe username/email for your logs
}

public class PublicStartLoginResponse
{
    public bool   RequiresInvoice { get; set; }
    public Guid   SessionId       { get; set; }
    public long   AmountSats      { get; set; }
    public string? Invoice        { get; set; }       // BOLT11 or null for TEST
    public string? PaymentHashB64 { get; set; }       // r_hash (base64) or null for TEST
}

public class PublicConfirmLoginRequest
{
    public Guid   SessionId      { get; set; }
    public string? PaymentHashB64 { get; set; } // for LIVE; optional in TEST
}

public class PublicConfirmLoginResponse
{
    public bool   Success      { get; set; }
    public string? Token       { get; set; }
    public long   SatsCharged  { get; set; }
    public string Environment  { get; set; } = "TEST";
}
