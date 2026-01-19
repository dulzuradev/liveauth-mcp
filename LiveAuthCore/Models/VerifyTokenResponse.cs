namespace LiveAuthCore.Models;

public class VerifyTokenResponse
{
    public bool Valid { get; set; }
    public Dictionary<string,string>? Claims { get; set; }
}