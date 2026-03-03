using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LiveAuthCore.Controllers;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous] // Health checks should be public
public class HealthController : ControllerBase
{
    private readonly Services.LightningService _lightning;
    private readonly IConfiguration _configuration;
    private readonly ILogger<HealthController> _logger;

    public HealthController(
        Services.LightningService lightning,
        IConfiguration configuration,
        ILogger<HealthController> logger)
    {
        _lightning = lightning;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Health check endpoint that verifies LND connectivity and returns system status.
    /// Use this to verify the API is running and Lightning is accessible.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetHealth(CancellationToken ct)
    {
        var health = new HealthResponse
        {
            Status = "healthy",
            Timestamp = DateTime.UtcNow
        };

        // Check LND connectivity
        try
        {
            var lndInfo = await _lightning.GetLndInfoAsync(ct);
            health.Lnd = new LndHealth
            {
                Connected = true,
                Version = lndInfo.Version,
                BlockHeight = lndInfo.BlockHeight,
                NumChannels = lndInfo.NumActiveChannels,
                NumPeers = lndInfo.NumPeers
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LND health check failed");
            health.Lnd = new LndHealth
            {
                Connected = false,
                Error = ex.Message
            };
            health.Status = "degraded";
        }

        // Check database connectivity
        try
        {
            // Simple DB check - just verify we can read config
            var dbPath = _configuration["ConnectionStrings:Default"];
            health.Database = new DatabaseHealth
            {
                Connected = !string.IsNullOrEmpty(dbPath),
                Provider = _configuration["DB_PROVIDER"] ?? "sqlite"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Database health check failed");
            health.Database = new DatabaseHealth
            {
                Connected = false,
                Error = ex.Message
            };
            health.Status = "degraded";
        }

        // Check critical configuration
        try
        {
            var demoProjectId = _configuration["LiveAuth:DemoProjectId"];
            var jwtKey = _configuration["Jwt:SigningKey"] ?? _configuration["Jwt:Key"];
            var lndUseMock = _configuration["Lnd:UseMock"]?.ToLowerInvariant() == "true";
            
            health.Config = new ConfigHealth
            {
                DemoProjectIdConfigured = !string.IsNullOrEmpty(demoProjectId),
                JwtKeyConfigured = !string.IsNullOrEmpty(jwtKey),
                LndUseMock = lndUseMock
            };
            
            if (string.IsNullOrEmpty(demoProjectId))
            {
                health.Warnings.Add("DemoProjectId not configured - demo auth will fail");
            }
            if (string.IsNullOrEmpty(jwtKey))
            {
                health.Warnings.Add("JWT signing key not configured");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Config health check failed");
        }

        return Ok(health);
    }

    /// <summary>
    /// Simple ping endpoint for load balancers.
    /// </summary>
    [HttpGet("ping")]
    public IActionResult Ping() => Ok(new { status = "ok", timestamp = DateTime.UtcNow });

    public class HealthResponse
    {
        public string Status { get; set; } = "unknown";
        public DateTime Timestamp { get; set; }
        public LndHealth? Lnd { get; set; }
        public DatabaseHealth? Database { get; set; }
        public ConfigHealth? Config { get; set; }
        public List<string> Warnings { get; set; } = new();
    }

    public class LndHealth
    {
        public bool Connected { get; set; }
        public string? Version { get; set; }
        public long BlockHeight { get; set; }
        public int NumChannels { get; set; }
        public int NumPeers { get; set; }
        public string? Error { get; set; }
    }

    public class DatabaseHealth
    {
        public bool Connected { get; set; }
        public string Provider { get; set; } = "unknown";
        public string? Error { get; set; }
    }

    public class ConfigHealth
    {
        public bool DemoProjectIdConfigured { get; set; }
        public bool JwtKeyConfigured { get; set; }
        public bool LndUseMock { get; set; }
    }
}
