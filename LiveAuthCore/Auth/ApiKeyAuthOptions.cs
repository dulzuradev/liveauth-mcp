using Microsoft.AspNetCore.Authentication;

namespace LiveAuthCore.Auth;

public class ApiKeyAuthOptions : AuthenticationSchemeOptions
{
    public const string SchemeName = "ApiKey";
}