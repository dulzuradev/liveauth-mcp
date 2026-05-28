using System.Net.Mail;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Controllers;

[ApiController]
[Route("api/public/waitlist")]
[AllowAnonymous]
[EnableRateLimiting("auth:x10")]
public class WaitlistController : ControllerBase
{
    private readonly LiveAuthDbContext _db;

    public WaitlistController(LiveAuthDbContext db)
    {
        _db = db;
    }

    [HttpPost]
    public async Task<ActionResult<WaitlistLeadResponse>> Join(
        [FromBody] WaitlistLeadRequest? request,
        CancellationToken ct)
    {
        var email = Normalize(request?.Email).ToLowerInvariant();
        var useCase = Normalize(request?.UseCase);
        var githubOrTwitter = Truncate(NormalizeOptional(request?.GithubOrTwitter), 200);
        var source = Truncate(NormalizeOptional(request?.Source), 100) ?? "liveauth.app";

        if (!IsValidEmail(email))
            return BadRequest(new { error = "valid_email_required" });

        if (string.IsNullOrWhiteSpace(useCase))
            return BadRequest(new { error = "use_case_required" });

        if (useCase.Length > 2000)
            return BadRequest(new { error = "use_case_too_long" });

        var now = DateTime.UtcNow;
        var lead = await _db.WaitlistLeads.SingleOrDefaultAsync(l => l.Email == email, ct);

        if (lead == null)
        {
            lead = new WaitlistLead
            {
                Id = Guid.NewGuid(),
                Email = email,
                CreatedAt = now
            };
            _db.WaitlistLeads.Add(lead);
        }

        lead.UseCase = useCase;
        lead.GithubOrTwitter = githubOrTwitter;
        lead.Source = source;
        lead.UserAgent = Request.Headers.UserAgent.ToString() is { Length: > 0 } userAgent
            ? userAgent[..Math.Min(userAgent.Length, 500)]
            : null;
        lead.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);

        return Ok(new WaitlistLeadResponse(lead.Id, "joined"));
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Trim();

    private static string? NormalizeOptional(string? value)
    {
        var normalized = Normalize(value);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;

        return value[..maxLength];
    }

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 320)
            return false;

        try
        {
            var parsed = new MailAddress(email);
            return string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}

public record WaitlistLeadRequest(
    string? Email,
    string? UseCase,
    string? GithubOrTwitter,
    string? Source);

public record WaitlistLeadResponse(Guid Id, string Status);
