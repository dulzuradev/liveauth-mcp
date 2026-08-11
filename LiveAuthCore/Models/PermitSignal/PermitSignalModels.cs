using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using LiveAuthCore.Data.Entities.PermitSignal;

namespace LiveAuthCore.Models.PermitSignal;

public sealed class SearchProjectsRequest
{
    [JsonPropertyName("location")]
    [MaxLength(160)]
    public string? Location { get; set; }

    [JsonPropertyName("municipality")]
    [MaxLength(120)]
    public string? Municipality { get; set; }

    [JsonPropertyName("state")]
    [MaxLength(2)]
    public string? State { get; set; }

    [JsonPropertyName("issued_after")]
    public DateTime? IssuedAfter { get; set; }

    [JsonPropertyName("issued_before")]
    public DateTime? IssuedBefore { get; set; }

    [JsonPropertyName("minimum_project_value")]
    [Range(0, double.MaxValue)]
    public decimal? MinimumProjectValue { get; set; }

    [JsonPropertyName("maximum_project_value")]
    [Range(0, double.MaxValue)]
    public decimal? MaximumProjectValue { get; set; }

    [JsonPropertyName("permit_type")]
    [MaxLength(200)]
    public string? PermitType { get; set; }

    [JsonPropertyName("work_category")]
    [MaxLength(80)]
    public string? WorkCategory { get; set; }

    [JsonPropertyName("commercial_only")]
    public bool CommercialOnly { get; set; }

    [JsonPropertyName("residential_only")]
    public bool ResidentialOnly { get; set; }

    [JsonPropertyName("keywords")]
    [MaxLength(300)]
    public string? Keywords { get; set; }

    [JsonPropertyName("contractor_name")]
    [MaxLength(200)]
    public string? ContractorName { get; set; }

    [JsonPropertyName("limit")]
    [Range(1, 100)]
    public int Limit { get; set; } = 25;
}

public sealed class FindOpportunitiesRequest
{
    [JsonPropertyName("location")]
    [MaxLength(160)]
    public string? Location { get; set; }

    [JsonPropertyName("state")]
    [MaxLength(2)]
    public string? State { get; set; }

    [JsonPropertyName("trade")]
    [Required, MaxLength(80)]
    public string Trade { get; set; } = string.Empty;

    [JsonPropertyName("issued_within_days")]
    [Range(1, 3650)]
    public int IssuedWithinDays { get; set; } = 7;

    [JsonPropertyName("minimum_project_value")]
    [Range(0, double.MaxValue)]
    public decimal? MinimumProjectValue { get; set; }

    [JsonPropertyName("commercial_only")]
    public bool CommercialOnly { get; set; }

    [JsonPropertyName("limit")]
    [Range(1, 100)]
    public int Limit { get; set; } = 25;
}

public sealed class AnalyzeProjectRequest
{
    [JsonPropertyName("project_id")]
    [Required, MaxLength(200)]
    public string ProjectId { get; set; } = string.Empty;
}

public sealed class PropertyHistoryRequest
{
    [JsonPropertyName("address")]
    [Required, MaxLength(500)]
    public string Address { get; set; } = string.Empty;

    [JsonPropertyName("municipality")]
    [MaxLength(120)]
    public string? Municipality { get; set; }

    [JsonPropertyName("state")]
    [MaxLength(2)]
    public string? State { get; set; }

    [JsonPropertyName("limit")]
    [Range(1, 100)]
    public int Limit { get; set; } = 50;
}

public sealed record PermitSourceDto(string Identifier, string Municipality, string State,
    string? RecordUrl, DateTime? LastSourceUpdate);

public sealed record PermitProjectDto(
    Guid Id,
    string SourceRecordId,
    string Municipality,
    string State,
    string Address,
    decimal? Latitude,
    decimal? Longitude,
    string PermitNumber,
    string? PermitType,
    string? PermitSubtype,
    string? Description,
    string? Status,
    DateTime? ApplicationDate,
    DateTime? IssueDate,
    DateTime? ExpirationDate,
    decimal? EstimatedProjectValue,
    string? ContractorName,
    string? ContractorLicense,
    string? OwnerName,
    string? ResidentialOrCommercial,
    IReadOnlyList<string> Categories,
    PermitSourceDto Source)
{
    public static PermitProjectDto FromEntity(PermitProject project) => new(
        project.Id,
        project.SourceRecordId,
        project.Municipality,
        project.State,
        project.Address,
        project.Latitude,
        project.Longitude,
        project.PermitNumber,
        project.PermitType,
        project.PermitSubtype,
        project.Description,
        project.Status,
        project.ApplicationDate,
        project.IssueDate,
        project.ExpirationDate,
        project.EstimatedProjectValue,
        project.ContractorName,
        project.ContractorLicense,
        project.OwnerName,
        project.ResidentialOrCommercial,
        project.Categories.Select(category => category.Category).OrderBy(category => category).ToArray(),
        new PermitSourceDto(project.Source, project.Municipality, project.State,
            project.RawSourceUrl, project.LastSourceUpdate));
}

public sealed record SearchProjectsResponse(int Count, IReadOnlyList<PermitProjectDto> Projects);

public sealed record OpportunityScore(int Score, string Level, string MatchedTrade,
    string MatchStrength, IReadOnlyList<string> Reasons);

public sealed record PermitOpportunity(PermitProjectDto Project, int OpportunityScore,
    string OpportunityLevel, string MatchedTrade, IReadOnlyList<string> Reasons,
    decimal? ProjectValue, int? PermitAgeDays, IReadOnlyList<string> Categories,
    PermitSourceDto Source);

public sealed record FindOpportunitiesResponse(int Count, IReadOnlyList<PermitOpportunity> Opportunities);

public sealed record ProjectAnalysis(
    PermitProjectDto Project,
    string Summary,
    string? PermitScope,
    string ProjectStage,
    int? PermitAgeDays,
    IReadOnlyList<string> LikelyTrades,
    IReadOnlyList<string> PotentialSupplierOrServiceOpportunities,
    IReadOnlyList<string> OpportunitySignals,
    IReadOnlyList<PermitSourceDto> SourceRecords);

public sealed record PropertyHistoryResponse(
    string RequestedAddress,
    string NormalizedAddress,
    string MatchConfidence,
    int TotalPermits,
    DateTime? FirstPermitDate,
    DateTime? MostRecentPermitDate,
    decimal TotalKnownPermittedValue,
    IReadOnlyList<string> CommonWorkCategories,
    IReadOnlyList<PermitProjectDto> MajorProjects,
    IReadOnlyList<PermitProjectDto> Permits);
