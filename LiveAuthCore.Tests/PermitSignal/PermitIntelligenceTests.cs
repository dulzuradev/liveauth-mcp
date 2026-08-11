using LiveAuthCore.Data.Entities.PermitSignal;
using LiveAuthCore.Services.PermitSignal;
using Xunit;

namespace LiveAuthCore.Tests.PermitSignal;

public sealed class PermitIntelligenceTests
{
    private readonly PermitCategoryClassifier _classifier = new();

    [Theory]
    [InlineData("Mechanical Permit", "Replace rooftop units and ductwork", PermitWorkCategories.Hvac)]
    [InlineData("Electrical", "Service upgrade from 200A to 600A", PermitWorkCategories.Electrical)]
    [InlineData("Building", "Reroof commercial building", PermitWorkCategories.Roofing)]
    [InlineData("Plumbing", "Install fixtures and new water line", PermitWorkCategories.Plumbing)]
    [InlineData("Fire", "Install fire sprinkler suppression system", PermitWorkCategories.FireProtection)]
    [InlineData("Building", "Construct new office building", PermitWorkCategories.NewConstruction)]
    public void Classifier_detects_expected_category(string type, string description, string expected)
        => Assert.Contains(expected, _classifier.Classify(type, null, description));

    [Fact]
    public void Classifier_can_assign_multiple_categories()
    {
        var categories = _classifier.Classify("Building", "Tenant Improvement",
            "Commercial renovation with electrical wiring and plumbing fixtures");
        Assert.Contains(PermitWorkCategories.TenantImprovement, categories);
        Assert.Contains(PermitWorkCategories.Electrical, categories);
        Assert.Contains(PermitWorkCategories.Plumbing, categories);
    }

    [Theory]
    [InlineData("760 14th Street, Apt 2", "760 14TH ST UNIT 2")]
    [InlineData("500 Congress Avenue", "500 CONGRESS AVE")]
    [InlineData("7429 Pullman Cove", "7429 PULLMAN CV")]
    public void Address_normalization_is_deterministic(string input, string expected)
        => Assert.Equal(expected, new AddressNormalizer().Normalize(input));

    [Fact]
    public void Recent_high_value_commercial_trade_match_scores_higher_than_old_residential_project()
    {
        var service = new OpportunityScoringService(PermitSignalTestFixture.Options());
        var now = new DateTime(2026, 8, 7, 0, 0, 0, DateTimeKind.Utc);
        var high = Project(now.AddDays(-2), 1_500_000, "Commercial", PermitWorkCategories.Electrical,
            PermitWorkCategories.NewConstruction);
        var low = Project(now.AddDays(-100), 20_000, "Residential", PermitWorkCategories.Other);

        var highScore = service.Score(high, "Electrical", now);
        var lowScore = service.Score(low, "Electrical", now);

        Assert.True(highScore.Score > lowScore.Score);
        Assert.Equal("High", highScore.Level);
        Assert.Contains(highScore.Reasons, reason => reason.Contains("Strong Electrical scope match"));
        Assert.NotEmpty(highScore.Reasons);
    }

    [Fact]
    public void Score_is_capped_and_every_awarded_signal_is_explained()
    {
        var service = new OpportunityScoringService(PermitSignalTestFixture.Options());
        var now = new DateTime(2026, 8, 7, 0, 0, 0, DateTimeKind.Utc);
        var score = service.Score(Project(now, 50_000_000, "Commercial", PermitWorkCategories.Hvac,
            PermitWorkCategories.NewConstruction), "HVAC", now);
        Assert.InRange(score.Score, 0, 100);
        Assert.True(score.Reasons.Count >= 5);
    }

    private static PermitProject Project(DateTime issueDate, decimal value, string occupancy, params string[] categories)
        => new()
        {
            IssueDate = issueDate, EstimatedProjectValue = value, ResidentialOrCommercial = occupancy,
            Categories = categories.Select(category => new PermitProjectCategory { Category = category }).ToList()
        };
}
