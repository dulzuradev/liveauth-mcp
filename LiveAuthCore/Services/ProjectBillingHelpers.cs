using LiveAuthCore.Data.Entities;

namespace LiveAuthCore.Services;

public static class ProjectBillingHelpers
{
    /// <summary>
    /// Computes the effective sats per login for a project, respecting environment.
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

        // LIVE: use configured sats; clamp to non-negative
        var configured = project.SatsPerLogin;
        if (configured < 0) configured = 0;

        return configured;
    }

    /// <summary>
    /// Should this project require a real Lightning invoice for logins?
    /// </summary>
    public static bool RequiresLightningPayment(Project project)
    {
        return GetEffectiveSatsPerLogin(project) > 0;
    }
}

public static class PlanLimits
{
    public const int FreeMonthlyAuthLimit = 1000;
    public const long FreeMaxSatsPerAuth = 21;
}
