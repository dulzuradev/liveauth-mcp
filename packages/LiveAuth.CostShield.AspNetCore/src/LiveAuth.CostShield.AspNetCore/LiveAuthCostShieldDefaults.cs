namespace LiveAuth.CostShield.AspNetCore;

internal static class LiveAuthCostShieldDefaults
{
    public const string HttpClientName =
        "LiveAuth.CostShield.AspNetCore";
    public const int MaximumTokenLength = 8 * 1024;
    public const string TokenType = "costshield+jwt";
}
