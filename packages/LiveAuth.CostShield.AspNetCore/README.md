# LiveAuth CostShield for ASP.NET Core

Protect expensive ASP.NET Core controller actions with locally verified,
action-bound LiveAuth CostShield tokens. Public signing keys are cached and
refreshed automatically. Single-use tokens are consumed through the LiveAuth
API before the action runs.

## Install

```bash
dotnet add package LiveAuth.CostShield.AspNetCore --version 0.1.0
```

## Configure

Register CostShield once during application startup:

```csharp
using LiveAuth.CostShield.AspNetCore;

builder.Services.AddLiveAuthCostShield(options =>
{
    options.ProjectId = Guid.Parse(
        builder.Configuration["LiveAuth:ProjectId"]!);
    options.Environment = LiveAuthCostShieldEnvironment.Test;
    options.SecretKey =
        builder.Configuration["LiveAuth:SecretKey"];
});
```

Keep the secret key on the server. It is required when a protected action uses
single-use authorizations. Use the matching `Test` or `Live` environment for
each deployment.

## Protect a controller action

```csharp
using LiveAuth.CostShield.AspNetCore;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/reports")]
public sealed class ReportsController : ControllerBase
{
    [HttpPost]
    [LiveAuthProtected(
        "generate-report",
        Origin = "https://app.example.com")]
    public IActionResult Generate()
    {
        var costShield =
            HttpContext.GetLiveAuthCostShieldAuthorization();

        return Ok(new
        {
            authorizationId =
                costShield?.Remote?.AuthorizationId,
            action = costShield?.Claims.Action
        });
    }
}
```

The browser sends its CostShield JWT as a bearer token:

```http
Authorization: Bearer <costshield-token>
```

The attribute verifies the signature, issuer, audience, lifetime, project,
environment, action, and optional origin. Its default `Consume = Auto` mode
validates reusable tokens locally and remotely consumes single-use tokens.

## Verify manually

Inject `ILiveAuthCostShieldVerifier` when endpoint filters, minimal APIs, or
custom middleware are a better fit:

```csharp
var authorization = await verifier.AuthorizeAsync(
    token,
    action: "generate-report",
    origin: "https://app.example.com",
    cancellationToken: cancellationToken);
```

`VerifyAsync` performs local verification only. Do not use it to authorize a
single-use action unless your application separately consumes the token.

## Local development

To point the package at a self-hosted LiveAuth API, set `ApiUrl` and `Issuer`
to that API's URL. `Audience` defaults to `liveauth-costshield`.

```csharp
options.ApiUrl = new Uri("https://localhost:5001");
options.Issuer = "https://localhost:5001";
```
