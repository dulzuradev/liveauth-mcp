using System.Text;
using System.Security.Claims;
using LiveAuthCore.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace LiveAuthCore.Extensions;

public static class AuthExtensions
{
    /// <summary>
    /// Adds LiveAuth authentication (API Key OR JWT).
    /// Uses a policy scheme to forward to the appropriate handler based on the Authorization header.
    /// </summary>
    public static WebApplicationBuilder AddLiveAuthAuth(this WebApplicationBuilder builder)
    {
        builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = "LiveAuthPolicy";
                options.DefaultChallengeScheme = "LiveAuthPolicy";
            })
            .AddPolicyScheme("LiveAuthPolicy", "API Key or JWT", options =>
            {
                options.ForwardDefaultSelector = context =>
                {
                    var auth = context.Request.Headers["Authorization"].ToString();
                    return auth.StartsWith("Bearer la_sk_", StringComparison.OrdinalIgnoreCase)
                        ? ApiKeyAuthOptions.SchemeName
                        : JwtBearerDefaults.AuthenticationScheme;
                };
            })
            .AddScheme<ApiKeyAuthOptions, ApiKeyAuthHandler>(
                ApiKeyAuthOptions.SchemeName, _ => { })
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var jwtKey =
                    builder.Configuration["Jwt:SigningKey"] ??
                    builder.Configuration["Jwt:Key"];

                if (string.IsNullOrWhiteSpace(jwtKey))
                    throw new InvalidOperationException("JWT signing key missing.");

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey =
                        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),

                    ValidateIssuer = true,
                    ValidIssuer = "LiveAuth",

                    ValidateAudience = true,
                    ValidAudience = "LiveAuthUsers",

                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),

                    RoleClaimType = ClaimTypes.Role,
                    NameClaimType = "userId"
                };

                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = ctx =>
                    {
                        Console.Error.WriteLine($"JWT auth failed: {ctx.Exception}");
                        return Task.CompletedTask;
                    }
                };
            });

        builder.Services.AddAuthorization();

        return builder;
    }
}
