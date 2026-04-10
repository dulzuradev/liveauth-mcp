using LiveAuthCore.Auth;
using LiveAuthCore.Controllers;
using LiveAuthCore.Extensions;
using LiveAuthCore.Middleware;
using LiveAuthCore.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

// --------------------------------------------------
// CONFIGURATION VALIDATION - Fail fast on missing required config
// --------------------------------------------------
var builder = WebApplication.CreateBuilder(args);

// Validate required configs
var missingConfigs = builder.GetMissingConfigs();
if (missingConfigs.Any())
{
    var error = $"[FATAL] Missing required configuration: {string.Join(", ", missingConfigs)}. Set via environment variables.";
    Console.Error.WriteLine(error);
    
    if (builder.Environment.IsDevelopment() || missingConfigs.Contains("LiveAuth:DemoProjectId") || missingConfigs.Contains("LiveAuth:PowHmacSecret"))
    {
        throw new InvalidOperationException(error);
    }
}

// Validate Lightning config
builder.ValidateLightningConfig();
builder.LogConfigState();

// --------------------------------------------------
// SERVICE REGISTRATION
// --------------------------------------------------
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    })
    .AddApplicationPart(typeof(HealthController).Assembly);

builder.AddLiveAuthDb();
builder.AddLiveAuthServices();
builder.AddLiveAuthCors();
builder.AddLiveAuthAuth();
builder.AddLiveAuthSwagger();

var app = builder.Build();

// --------------------------------------------------
// DATABASE INITIALIZATION
// --------------------------------------------------
await app.InitializeDatabaseAsync();

// --------------------------------------------------
// PIPELINE
// --------------------------------------------------
app.UseLiveAuthExceptionHandler();
app.UseLiveAuthPipeline();
app.Run();

// Make Program class accessible to test projects
public partial class Program { }
