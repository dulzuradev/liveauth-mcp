using LiveAuthCore.Data.Entities;

namespace LiveAuthCore.Services;

public static class PlanLimits
{
    // Free tier: 1,000 verifications/month
    public const int FreeMonthlyAuthLimit = 1_000;
    public const long FreeMaxSatsPerAuth = 21;

    // Pro tier: 100,000 verifications/month
    public const int ProMonthlyAuthLimit = 100_000;
    public const long ProMaxSatsPerAuth = 21;

    public const int FreeProtectedActionLimit = 3;
    public const int ProProtectedActionLimit = 25;

    // Enterprise: unlimited (use int.MaxValue)
    public const int EnterpriseMonthlyAuthLimit = int.MaxValue;

    public static int GetMonthlyAuthLimit(string plan, DateTime? proPaidUntil)
    {
        return plan.ToLowerInvariant() switch
        {
            "pro" when proPaidUntil.HasValue && proPaidUntil.Value > DateTime.UtcNow 
                => ProMonthlyAuthLimit,
            "enterprise" => EnterpriseMonthlyAuthLimit,
            _ => FreeMonthlyAuthLimit
        };
    }

    public static long GetMaxSatsPerAuth(string plan)
    {
        return plan.ToLowerInvariant() switch
        {
            "pro" => ProMaxSatsPerAuth,
            "enterprise" => 100, // Higher limit for enterprise
            _ => FreeMaxSatsPerAuth
        };
    }

    public static bool IsActivePro(string plan, DateTime? proPaidUntil)
    {
        return plan.ToLowerInvariant() == "pro" && 
               proPaidUntil.HasValue && 
               proPaidUntil.Value > DateTime.UtcNow;
    }

    public static int GetProtectedActionLimit(string plan, DateTime? proPaidUntil)
    {
        return plan.ToLowerInvariant() switch
        {
            "pro" when proPaidUntil.HasValue && proPaidUntil.Value > DateTime.UtcNow
                => ProProtectedActionLimit,
            "enterprise" => int.MaxValue,
            _ => FreeProtectedActionLimit
        };
    }
}

public static class ProjectBillingHelpers
{
    /// <summary>
    /// Gets the monthly auth limit for a project based on its plan.
    /// </summary>
    public static int GetMonthlyAuthLimit(Project project)
    {
        return PlanLimits.GetMonthlyAuthLimit(project.Plan, project.ProPaidUntil);
    }

    /// <summary>
    /// Gets the effective sats per login for a project, respecting environment.
    /// TEST => always 0 sats (no Lightning payment required).
    /// LIVE => uses stored SatsPerLogin (clamped to >= 0).
    /// </summary>
    public static long GetEffectiveSatsPerLogin(Project project)
    {
        var env = (project.Environment ?? "TEST").Trim().ToUpperInvariant();

        if (env == "TEST")
        {
            return 0L;
        }

        // LIVE: use configured sats; clamp to non-negative and max for plan
        var configured = project.SatsPerLogin;
        if (configured < 0) configured = 0;

        var maxSats = PlanLimits.GetMaxSatsPerAuth(project.Plan);
        if (configured > maxSats) configured = (int)maxSats;

        return configured;
    }

    /// <summary>
    /// Should this project require a real Lightning invoice for logins?
    /// </summary>
    public static bool RequiresLightningPayment(Project project)
    {
        return GetEffectiveSatsPerLogin(project) > 0;
    }

    /// <summary>
    /// Checks if the project's monthly usage has exceeded its quota.
    /// Also handles monthly reset if we're in a new billing period.
    /// </summary>
    public static (bool Allowed, string? Reason) CheckMonthlyQuota(Project project)
    {
        // Reset monthly count if we're in a new month
        var now = DateTime.UtcNow;
        var periodStart = project.MonthlyAuthPeriodStart;
        
        // Reset on the 1st of each month
        if (periodStart.Month != now.Month || periodStart.Year != now.Year)
        {
            // This will be handled by the service that calls this
            return (true, null); // Signal that reset is needed
        }

        var limit = GetMonthlyAuthLimit(project);
        
        if (project.MonthlyAuthCount >= limit)
        {
            var plan = project.Plan.ToLowerInvariant();
            var upgrade = plan == "free" ? "Pro" : "Enterprise";
            return (false, $"Monthly {limit:N0} verification limit exceeded. Upgrade to {upgrade} for more.");
        }

        return (true, null);
    }
}
