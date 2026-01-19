namespace LiveAuthCore.Models;

public class AuthConfirmResponse
{
    public bool Verified { get; set; }
    public string? Token { get; set; }
    public string Method { get; set; } = "lightning";
    public int ExpiresIn { get; set; } = 300;
}