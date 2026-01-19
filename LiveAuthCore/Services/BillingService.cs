using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;

namespace LiveAuthCore.Services;

public sealed class BillingService
{
    private static readonly TimeSpan ProGracePeriod = TimeSpan.FromDays(7);

    public bool EnsurePlanIsCurrent(Project project, DateTime nowUtc)
    {
        if (project.Plan != "pro")
            return false;

        if (project.ProPaidUntil == null)
        {
            Downgrade(project);
            return true;
        }

        // Still fully active
        if (project.ProPaidUntil >= nowUtc)
            return false;

        // Grace window
        if (project.ProPaidUntil.Value.Add(ProGracePeriod) >= nowUtc)
        {
            // Stay PRO, but restricted
            return false;
        }

        // Grace expired → downgrade
        Downgrade(project);
        return true;
    }

    public bool IsInGracePeriod(Project project, DateTime nowUtc)
    {
        return project.Plan == "pro"
               && project.ProPaidUntil != null
               && project.ProPaidUntil < nowUtc
               && project.ProPaidUntil.Value.Add(ProGracePeriod) >= nowUtc;
    }

    private static void Downgrade(Project project)
    {
        project.Plan = "free";
        project.ProPaidUntil = null;
        project.MonthlyQuota = PlanLimits.FreeMonthlyAuthLimit;
    }
}
