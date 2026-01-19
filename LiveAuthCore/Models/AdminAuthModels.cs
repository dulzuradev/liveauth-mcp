namespace LiveAuthCore.Models;

public sealed class AdminStartLoginRequest
{
    public string Email { get; set; } = string.Empty;
}

public sealed class AdminStartLoginResponse
{
    public Guid SessionId { get; set; }
    public string Invoice { get; set; } = string.Empty;
    public long AmountSats { get; set; }
    public long ExpiresAtUnix { get; set; }
}

public sealed class AdminConfirmLoginRequest
{
    public Guid SessionId { get; set; }
}

public sealed class AdminConfirmLoginResponse
{
    public bool Verified { get; set; }
    public string? Token { get; set; }
    public long? ExpiresAtUnix { get; set; }
}