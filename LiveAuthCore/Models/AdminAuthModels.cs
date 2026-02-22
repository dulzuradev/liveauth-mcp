namespace LiveAuthCore.Models;

using System.ComponentModel.DataAnnotations;

public sealed class AdminStartLoginRequest
{
    /// <summary>
    /// Admin email address.
    /// </summary>
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Email must be a valid email address")]
    [StringLength(255, ErrorMessage = "Email cannot exceed 255 characters")]
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
    [Required(ErrorMessage = "SessionId is required")]
    public Guid SessionId { get; set; }
}

public sealed class AdminConfirmLoginResponse
{
    public bool Verified { get; set; }
    public string? Token { get; set; }
    public long? ExpiresAtUnix { get; set; }
}
