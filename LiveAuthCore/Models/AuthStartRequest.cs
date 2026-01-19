namespace LiveAuthCore.Models;

public class AuthStartRequest
{
    public string UserRef { get; set; } = string.Empty;
    public long AmountSats { get; set; } = 200;
    public string Memo { get; set; } = "LiveAuth human verification";
}