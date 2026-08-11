using LiveAuthCore.Data.Entities.PermitSignal;
using LiveAuthCore.Models.PermitSignal;
using Microsoft.Extensions.Options;

namespace LiveAuthCore.Services.PermitSignal;

public interface IOpportunityScoringService
{
    OpportunityScore Score(PermitProject project, string trade, DateTime? nowUtc = null);
}

public sealed class OpportunityScoringService : IOpportunityScoringService
{
    private readonly PermitSignalScoringOptions _options;

    public OpportunityScoringService(IOptions<PermitSignalOptions> options)
        => _options = options.Value.Scoring;

    public OpportunityScore Score(PermitProject project, string trade, DateTime? nowUtc = null)
    {
        var matchedTrade = TradeCategoryNormalizer.Normalize(trade)
            ?? throw new PermitSignalValidationException($"Unsupported trade '{trade}'.");
        var categories = project.Categories.Select(category => category.Category).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (categories.Count == 0 && !string.IsNullOrWhiteSpace(project.WorkCategory))
            categories.Add(project.WorkCategory);

        var reasons = new List<string>();
        var score = 0;
        var now = nowUtc ?? DateTime.UtcNow;

        if (project.IssueDate.HasValue)
        {
            var age = Math.Max(0, (int)(now.Date - project.IssueDate.Value.Date).TotalDays);
            if (age <= 3)
            {
                score += _options.IssuedWithin3Days;
                reasons.Add($"Permit issued {age} day{(age == 1 ? string.Empty : "s")} ago (+{_options.IssuedWithin3Days})");
            }
            else if (age <= 7)
            {
                score += _options.IssuedWithin7Days;
                reasons.Add($"Permit issued within 7 days (+{_options.IssuedWithin7Days})");
            }
            else if (age <= 30)
            {
                score += _options.IssuedWithin30Days;
                reasons.Add($"Permit issued within 30 days (+{_options.IssuedWithin30Days})");
            }
        }

        if (string.Equals(project.ResidentialOrCommercial, "Commercial", StringComparison.OrdinalIgnoreCase))
        {
            score += _options.Commercial;
            reasons.Add($"Commercial construction (+{_options.Commercial})");
        }

        if (project.EstimatedProjectValue >= 1_000_000)
        {
            score += _options.ValueOverOneMillion;
            reasons.Add($"Declared project value is at least 1,000,000 (+{_options.ValueOverOneMillion})");
        }
        else if (project.EstimatedProjectValue >= 250_000)
        {
            score += _options.ValueOverTwoHundredFiftyThousand;
            reasons.Add($"Declared project value is at least 250,000 (+{_options.ValueOverTwoHundredFiftyThousand})");
        }
        else if (project.EstimatedProjectValue >= 100_000)
        {
            score += _options.ValueOverOneHundredThousand;
            reasons.Add($"Declared project value is at least 100,000 (+{_options.ValueOverOneHundredThousand})");
        }

        var matchStrength = "None";
        if (categories.Contains(matchedTrade))
        {
            matchStrength = "Strong";
            score += _options.StrongTradeMatch;
            reasons.Add($"Strong {matchedTrade} scope match (+{_options.StrongTradeMatch})");
        }
        else if (IsWeakMatch(categories, matchedTrade))
        {
            matchStrength = "Weak";
            score += _options.WeakTradeMatch;
            reasons.Add($"Project type commonly involves {matchedTrade} work (+{_options.WeakTradeMatch})");
        }

        if (categories.Contains(PermitWorkCategories.NewConstruction))
        {
            score += _options.NewConstruction;
            reasons.Add($"New construction scope (+{_options.NewConstruction})");
        }
        else if (categories.Contains(PermitWorkCategories.Renovation) || categories.Contains(PermitWorkCategories.TenantImprovement))
        {
            score += _options.Renovation;
            reasons.Add($"Renovation or tenant-improvement scope (+{_options.Renovation})");
        }

        score = Math.Clamp(score, 0, 100);
        var level = score >= 70 ? "High" : score >= 40 ? "Medium" : "Low";
        return new OpportunityScore(score, level, matchedTrade, matchStrength, reasons);
    }

    private static bool IsWeakMatch(IReadOnlySet<string> categories, string trade)
    {
        if (categories.Contains(PermitWorkCategories.NewConstruction))
            return true;
        if (trade == PermitWorkCategories.Hvac && categories.Contains(PermitWorkCategories.Mechanical))
            return true;
        if (trade == PermitWorkCategories.Mechanical && categories.Contains(PermitWorkCategories.Hvac))
            return true;
        return categories.Contains(PermitWorkCategories.GeneralConstruction) &&
               trade is PermitWorkCategories.Electrical or PermitWorkCategories.Plumbing or
                   PermitWorkCategories.Hvac or PermitWorkCategories.FireProtection;
    }
}

public sealed class PermitSignalValidationException : Exception
{
    public PermitSignalValidationException(string message) : base(message) { }
}
