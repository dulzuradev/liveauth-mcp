using System.ComponentModel.DataAnnotations;

namespace LiveAuthCore.Data.Entities.PermitSignal;

public sealed class PermitProject
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PermitSourceId { get; set; }
    public PermitSource PermitSource { get; set; } = null!;

    [MaxLength(80)]
    public string Source { get; set; } = string.Empty;

    [MaxLength(200)]
    public string SourceRecordId { get; set; } = string.Empty;

    [MaxLength(120)]
    public string Municipality { get; set; } = string.Empty;

    [MaxLength(2)]
    public string State { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Address { get; set; } = string.Empty;

    [MaxLength(500)]
    public string NormalizedAddress { get; set; } = string.Empty;

    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }

    [MaxLength(120)]
    public string PermitNumber { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? PermitType { get; set; }

    [MaxLength(200)]
    public string? PermitSubtype { get; set; }

    public string? Description { get; set; }

    [MaxLength(120)]
    public string? Status { get; set; }

    public DateTime? ApplicationDate { get; set; }
    public DateTime? IssueDate { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public decimal? EstimatedProjectValue { get; set; }

    [MaxLength(300)]
    public string? ContractorName { get; set; }

    [MaxLength(120)]
    public string? ContractorLicense { get; set; }

    [MaxLength(300)]
    public string? OwnerName { get; set; }

    [MaxLength(32)]
    public string? ResidentialOrCommercial { get; set; }

    [MaxLength(80)]
    public string WorkCategory { get; set; } = PermitWorkCategories.Other;

    [MaxLength(1000)]
    public string? RawSourceUrl { get; set; }

    public DateTime? LastSourceUpdate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<PermitProjectCategory> Categories { get; set; } = new List<PermitProjectCategory>();
}

public sealed class PermitProjectCategory
{
    public Guid PermitProjectId { get; set; }
    public PermitProject PermitProject { get; set; } = null!;

    [MaxLength(80)]
    public string Category { get; set; } = PermitWorkCategories.Other;
}

public static class PermitWorkCategories
{
    public const string GeneralConstruction = "GeneralConstruction";
    public const string Roofing = "Roofing";
    public const string Hvac = "HVAC";
    public const string Electrical = "Electrical";
    public const string Plumbing = "Plumbing";
    public const string Solar = "Solar";
    public const string FireProtection = "FireProtection";
    public const string Mechanical = "Mechanical";
    public const string Structural = "Structural";
    public const string Demolition = "Demolition";
    public const string NewConstruction = "NewConstruction";
    public const string Renovation = "Renovation";
    public const string TenantImprovement = "TenantImprovement";
    public const string Other = "Other";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        GeneralConstruction, Roofing, Hvac, Electrical, Plumbing, Solar,
        FireProtection, Mechanical, Structural, Demolition, NewConstruction,
        Renovation, TenantImprovement, Other
    };

    public static string? Normalize(string? value)
        => All.FirstOrDefault(category => category.Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase));
}
