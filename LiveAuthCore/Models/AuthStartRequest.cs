namespace LiveAuthCore.Models;

using System.ComponentModel.DataAnnotations;

public class AuthStartRequest
{
    /// <summary>
    /// Unique identifier for the user being authenticated (e.g., email, user ID).
    /// </summary>
    [Required(ErrorMessage = "UserRef is required")]
    [StringLength(255, MinimumLength = 1, ErrorMessage = "UserRef must be between 1 and 255 characters")]
    public string UserRef { get; set; } = string.Empty;

    /// <summary>
    /// Satoshis to request for verification (200 sats default).
    /// </summary>
    [Range(1, 10000, ErrorMessage = "AmountSats must be between 1 and 10000")]
    public long AmountSats { get; set; } = 200;

    /// <summary>
    /// Memo/description for the Lightning invoice.
    /// </summary>
    [StringLength(500, ErrorMessage = "Memo cannot exceed 500 characters")]
    public string Memo { get; set; } = "LiveAuth human verification";
}
