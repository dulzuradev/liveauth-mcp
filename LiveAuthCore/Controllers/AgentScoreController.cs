using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Controllers;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous] // Agent score should be public for reputation checking
public class AgentScoreController : ControllerBase
{
    private readonly LiveAuthDbContext _db;

    public AgentScoreController(LiveAuthDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Get reputation score for an agent (identified by API key).
    /// Returns a score based on authentication history, payment history, and behavior.
    /// </summary>
    /// <param name="apiKey">The API key (public key like la_pk_xxx)</param>
    [HttpGet]
    public async Task<IActionResult> GetScore([FromQuery] string apiKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return BadRequest("apiKey is required");

        // Look up the API key
        var key = await _db.ProjectApiKeys
            .Include(k => k.Project)
            .FirstOrDefaultAsync(k => k.PublicKey == apiKey, ct);

        if (key == null)
            return NotFound("API key not found");

        // Get auth events for this key
        var events = await _db.AuthEvents
            .Where(e => e.ApiKeyId == key.Id)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(ct);

        // Calculate score
        var now = DateTime.UtcNow;
        var last7Days = now.AddDays(-7);
        var last30Days = now.AddDays(-30);
        var last90Days = now.AddDays(-90);

        var totalEvents = events.Count;
        var successfulAuths = events.Count(e => e.EventType == AuthEventType.LoginSucceeded || e.EventType == AuthEventType.PowSolved);
        var failedAuths = events.Count(e => e.EventType == AuthEventType.LoginFailed || e.EventType == AuthEventType.PowFailed);
        var totalSatsPaid = events.Where(e => e.SatsPaid.HasValue).Sum(e => e.SatsPaid!.Value);
        
        // Recent activity
        var last7DaysEvents = events.Count(e => e.CreatedAt >= last7Days);
        var last30DaysEvents = events.Count(e => e.CreatedAt >= last30Days);
        
        // PoW difficulty tracking (if available in reason/ClientIp)
        var powSolved = events.Count(e => e.EventType == AuthEventType.PowSolved);
        
        // Calculate score (0-100)
        var score = CalculateScore(successfulAuths, failedAuths, totalSatsPaid, last7DaysEvents, powSolved);

        // Build response
        var response = new AgentScoreResponse
        {
            ApiKey = apiKey,
            ProjectName = key.Project?.Name ?? "Unknown",
            Score = score,
            Level = GetLevel(score),
            TotalAuthentications = totalEvents,
            SuccessfulAuthentications = successfulAuths,
            FailedAuthentications = failedAuths,
            TotalSatsPaid = totalSatsPaid,
            LastActivityAt = events.FirstOrDefault()?.CreatedAt,
            FirstActivityAt = events.LastOrDefault()?.CreatedAt,
            Last7DaysActivity = last7DaysEvents,
            Last30DaysActivity = last30DaysEvents,
            Factors = new ScoreFactors
            {
                SuccessRate = totalEvents > 0 ? (double)successfulAuths / totalEvents * 100 : 0,
                PaymentContribution = Math.Min(totalSatsPaid / 100.0, 30), // Cap at 30 points
                ConsistencyContribution = Math.Min(last7DaysEvents / 10.0, 20), // Cap at 20 points
                HistoryContribution = Math.Min(totalEvents / 50.0, 20), // Cap at 20 points
            }
        };

        return Ok(response);
    }

    private static int CalculateScore(int successes, int failures, long satsPaid, int recentActivity, int powSolved)
    {
        var score = 0;

        // Base score from success rate (0-30 points)
        var total = successes + failures;
        if (total > 0)
        {
            var successRate = (double)successes / total;
            score += (int)(successRate * 30);
        }

        // Payment history (0-30 points)
        score += (int)Math.Min(satsPaid / 100.0, 30);

        // Consistency/recent activity (0-20 points)
        score += (int)Math.Min(recentActivity / 5.0, 20);

        // History length (0-20 points)
        score += (int)Math.Min(total / 20.0, 20);

        return Math.Min(score, 100);
    }

    private static string GetLevel(int score) => score switch
    {
        >= 90 => "trusted",
        >= 70 => "verified",
        >= 50 => "standard",
        >= 25 => "new",
        _ => "unknown"
    };
}

public class AgentScoreResponse
{
    public string ApiKey { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public int Score { get; set; }
    public string Level { get; set; } = "";
    public int TotalAuthentications { get; set; }
    public int SuccessfulAuthentications { get; set; }
    public int FailedAuthentications { get; set; }
    public long TotalSatsPaid { get; set; }
    public DateTime? LastActivityAt { get; set; }
    public DateTime? FirstActivityAt { get; set; }
    public int Last7DaysActivity { get; set; }
    public int Last30DaysActivity { get; set; }
    public ScoreFactors Factors { get; set; } = new();
}

public class ScoreFactors
{
    public double SuccessRate { get; set; }
    public double PaymentContribution { get; set; }
    public double ConsistencyContribution { get; set; }
    public double HistoryContribution { get; set; }
}
