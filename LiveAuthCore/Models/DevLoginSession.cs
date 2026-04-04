namespace LiveAuthCore.Models;


public class DevStartLoginRequest
{
    public string DeveloperEmail { get; set; } = string.Empty;
}

public class DevStartLoginResponse
{
    public Guid SessionId { get; set; }
    public string Invoice { get; set; } = string.Empty;
    public long AmountSats { get; set; }
    public long ExpiresAtUnix { get; set; }
    public string? DebugToken { get; set; }
}

public class DevConfirmLoginRequest
{
    public Guid SessionId { get; set; }
}

public class DevConfirmLoginResponse
{
    public bool Verified { get; set; }
    public string? Token { get; set; }
}
