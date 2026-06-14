using FluentAssertions;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Services;
using Xunit;

namespace LiveAuthCore.Tests.Services;

public class BillingServiceTests
{
    private readonly BillingService _service = new();

    [Fact]
    public void EnsurePlanIsCurrent_FreePlan_DoesNotChangeProject()
    {
        var project = new Project
        {
            Plan = "free",
            ProPaidUntil = DateTime.UtcNow.AddDays(30),
            MonthlyQuota = 123
        };

        var changed = _service.EnsurePlanIsCurrent(project, DateTime.UtcNow);

        changed.Should().BeFalse();
        project.Plan.Should().Be("free");
        project.MonthlyQuota.Should().Be(123);
    }

    [Fact]
    public void EnsurePlanIsCurrent_ActiveProPlan_DoesNotChangeProject()
    {
        var now = DateTime.UtcNow;
        var paidUntil = now.AddMinutes(1);
        var project = new Project
        {
            Plan = "pro",
            ProPaidUntil = paidUntil,
            MonthlyQuota = PlanLimits.ProMonthlyAuthLimit
        };

        var changed = _service.EnsurePlanIsCurrent(project, now);

        changed.Should().BeFalse();
        project.Plan.Should().Be("pro");
        project.ProPaidUntil.Should().Be(paidUntil);
        _service.IsInGracePeriod(project, now).Should().BeFalse();
    }

    [Fact]
    public void EnsurePlanIsCurrent_ExpiredWithinGracePeriod_KeepsProPlan()
    {
        var now = DateTime.UtcNow;
        var paidUntil = now.AddDays(-3);
        var project = new Project
        {
            Plan = "pro",
            ProPaidUntil = paidUntil,
            MonthlyQuota = PlanLimits.ProMonthlyAuthLimit
        };

        var changed = _service.EnsurePlanIsCurrent(project, now);

        changed.Should().BeFalse();
        project.Plan.Should().Be("pro");
        project.ProPaidUntil.Should().Be(paidUntil);
        _service.IsInGracePeriod(project, now).Should().BeTrue();
    }

    [Fact]
    public void EnsurePlanIsCurrent_ProWithoutPaidUntil_DowngradesToFree()
    {
        var project = new Project
        {
            Plan = "pro",
            ProPaidUntil = null,
            MonthlyQuota = PlanLimits.ProMonthlyAuthLimit
        };

        var changed = _service.EnsurePlanIsCurrent(project, DateTime.UtcNow);

        changed.Should().BeTrue();
        project.Plan.Should().Be("free");
        project.ProPaidUntil.Should().BeNull();
        project.MonthlyQuota.Should().Be(PlanLimits.FreeMonthlyAuthLimit);
    }

    [Fact]
    public void EnsurePlanIsCurrent_ExpiredBeyondGracePeriod_DowngradesToFree()
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Plan = "pro",
            ProPaidUntil = now.AddDays(-8),
            MonthlyQuota = PlanLimits.ProMonthlyAuthLimit
        };

        var changed = _service.EnsurePlanIsCurrent(project, now);

        changed.Should().BeTrue();
        project.Plan.Should().Be("free");
        project.ProPaidUntil.Should().BeNull();
        project.MonthlyQuota.Should().Be(PlanLimits.FreeMonthlyAuthLimit);
        _service.IsInGracePeriod(project, now).Should().BeFalse();
    }
}

public class ProjectBillingHelpersTests
{
    [Fact]
    public void GetMonthlyAuthLimit_ReturnsPlanSpecificLimits()
    {
        ProjectBillingHelpers.GetMonthlyAuthLimit(new Project { Plan = "free" })
            .Should().Be(PlanLimits.FreeMonthlyAuthLimit);

        ProjectBillingHelpers.GetMonthlyAuthLimit(new Project
            {
                Plan = "pro",
                ProPaidUntil = DateTime.UtcNow.AddDays(1)
            })
            .Should().Be(PlanLimits.ProMonthlyAuthLimit);

        ProjectBillingHelpers.GetMonthlyAuthLimit(new Project { Plan = "enterprise" })
            .Should().Be(PlanLimits.EnterpriseMonthlyAuthLimit);
    }

    [Fact]
    public void GetMonthlyAuthLimit_ExpiredProFallsBackToFree()
    {
        var limit = ProjectBillingHelpers.GetMonthlyAuthLimit(new Project
        {
            Plan = "pro",
            ProPaidUntil = DateTime.UtcNow.AddDays(-1)
        });

        limit.Should().Be(PlanLimits.FreeMonthlyAuthLimit);
    }

    [Theory]
    [InlineData("TEST", "free", 100, 0)]
    [InlineData(" test ", "free", 100, 0)]
    [InlineData("LIVE", "free", -5, 0)]
    [InlineData("LIVE", "free", 5, 5)]
    [InlineData("LIVE", "free", 500, PlanLimits.FreeMaxSatsPerAuth)]
    [InlineData("LIVE", "pro", 500, PlanLimits.ProMaxSatsPerAuth)]
    [InlineData("LIVE", "enterprise", 500, 100)]
    public void GetEffectiveSatsPerLogin_RespectsEnvironmentAndPlanCaps(
        string environment,
        string plan,
        int configuredSats,
        long expectedSats)
    {
        var project = new Project
        {
            Environment = environment,
            Plan = plan,
            SatsPerLogin = configuredSats
        };

        ProjectBillingHelpers.GetEffectiveSatsPerLogin(project).Should().Be(expectedSats);
        ProjectBillingHelpers.RequiresLightningPayment(project).Should().Be(expectedSats > 0);
    }

    [Fact]
    public void CheckMonthlyQuota_NewBillingPeriod_AllowsRequestSoCallerCanResetUsage()
    {
        var project = new Project
        {
            Plan = "free",
            MonthlyAuthCount = PlanLimits.FreeMonthlyAuthLimit,
            MonthlyAuthPeriodStart = DateTime.UtcNow.AddMonths(-1)
        };

        var result = ProjectBillingHelpers.CheckMonthlyQuota(project);

        result.Allowed.Should().BeTrue();
        result.Reason.Should().BeNull();
    }

    [Fact]
    public void CheckMonthlyQuota_WhenAtLimit_ReturnsUpgradeReason()
    {
        var project = new Project
        {
            Plan = "free",
            MonthlyAuthCount = PlanLimits.FreeMonthlyAuthLimit,
            MonthlyAuthPeriodStart = DateTime.UtcNow
        };

        var result = ProjectBillingHelpers.CheckMonthlyQuota(project);

        result.Allowed.Should().BeFalse();
        result.Reason.Should().Contain("Monthly 1,000 verification limit exceeded");
        result.Reason.Should().Contain("Upgrade to Pro");
    }

    [Fact]
    public void CheckMonthlyQuota_WhenBelowLimit_AllowsRequest()
    {
        var project = new Project
        {
            Plan = "free",
            MonthlyAuthCount = PlanLimits.FreeMonthlyAuthLimit - 1,
            MonthlyAuthPeriodStart = DateTime.UtcNow
        };

        var result = ProjectBillingHelpers.CheckMonthlyQuota(project);

        result.Allowed.Should().BeTrue();
        result.Reason.Should().BeNull();
    }
}
