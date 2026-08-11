using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities.Mcp;
using LiveAuthCore.Data.Entities.PermitSignal;
using LiveAuthCore.Models.PermitSignal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LiveAuthCore.Services.PermitSignal;

public interface IPermitSignalBootstrapper
{
    Task SeedAsync(CancellationToken ct = default);
}

public sealed class PermitSignalBootstrapper : IPermitSignalBootstrapper
{
    private readonly LiveAuthDbContext _db;
    private readonly PermitSignalOptions _options;
    private readonly IConfiguration _configuration;
    private readonly IPermitCategoryClassifier _classifier;
    private readonly IAddressNormalizer _addresses;

    public PermitSignalBootstrapper(LiveAuthDbContext db, IOptions<PermitSignalOptions> options,
        IConfiguration configuration, IPermitCategoryClassifier classifier, IAddressNormalizer addresses)
    {
        _db = db;
        _options = options.Value;
        _configuration = configuration;
        _classifier = classifier;
        _addresses = addresses;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await SeedToolsAsync(ct);
        if (_options.SeedDemoData)
            await SeedDemoDataAsync(ct);
    }

    private async Task SeedToolsAsync(CancellationToken ct)
    {
        Guid? projectId = null;
        var configuredProject = _configuration["PermitSignal:ProjectId"] ?? _configuration["LiveAuth:DemoProjectId"];
        if (Guid.TryParse(configuredProject, out var parsed)) projectId = parsed;

        var tools = new[]
        {
            Tool(Guid.Parse("00000000-0000-0000-0000-000000000006"), "PermitSignal Search Projects", "permitsignal-search-projects",
                "Search normalized public construction permits across supported municipalities with date, value, category, occupancy, keyword, and contractor filters. Paid operation.", _options.Tools.SearchProjects.PriceSats, projectId),
            Tool(Guid.Parse("00000000-0000-0000-0000-000000000007"), "PermitSignal Find Opportunities", "permitsignal-find-opportunities",
                "Find recently permitted projects that represent explainable sales opportunities for HVAC, electrical, plumbing, roofing, solar, fire protection, or construction suppliers. Paid operation.", _options.Tools.FindOpportunities.PriceSats, projectId),
            Tool(Guid.Parse("00000000-0000-0000-0000-000000000008"), "PermitSignal Analyze Project", "permitsignal-analyze-project",
                "Analyze one normalized permit project, including scope, stage, likely trades, supplier opportunities, opportunity signals, and official source provenance. Paid operation.", _options.Tools.AnalyzeProject.PriceSats, projectId),
            Tool(Guid.Parse("00000000-0000-0000-0000-000000000009"), "PermitSignal Property History", "permitsignal-property-history",
                "Retrieve exact-normalized permit history for a property with totals, date range, common work categories, major projects, and source provenance. Paid operation.", _options.Tools.PropertyHistory.PriceSats, projectId)
        };

        foreach (var configured in tools)
        {
            var existing = await _db.McpTools.SingleOrDefaultAsync(tool => tool.Slug == configured.Slug, ct);
            if (existing == null)
            {
                _db.McpTools.Add(configured);
                continue;
            }
            existing.ProjectId = configured.ProjectId;
            existing.Name = configured.Name;
            existing.Description = configured.Description;
            existing.DefaultCostSats = configured.DefaultCostSats;
            existing.MinCostSats = configured.DefaultCostSats;
            existing.MaxCostSats = configured.DefaultCostSats;
            existing.Status = "Active";
            existing.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
    }

    private async Task SeedDemoDataAsync(CancellationToken ct)
    {
        const string sourceId = "permitsignal-demo";
        var source = await _db.PermitSources.SingleOrDefaultAsync(item => item.SourceIdentifier == sourceId, ct);
        if (source == null)
        {
            source = new PermitSource
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000001"),
                SourceIdentifier = sourceId,
                Municipality = "Austin",
                State = "TX",
                AdapterType = "DeterministicDemoData",
                OfficialDatasetUrl = "https://data.austintexas.gov/Building-and-Development/Issued-Construction-Permits/3syk-w9eu",
                HealthStatus = "Demo",
                LastSuccessfulSync = new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc)
            };
            _db.PermitSources.Add(source);
            await _db.SaveChangesAsync(ct);
        }

        var records = DemoRecords();
        var existingIds = await _db.PermitProjects.Where(project => project.PermitSourceId == source.Id)
            .Select(project => project.SourceRecordId).ToListAsync(ct);
        foreach (var record in records.Where(record => !existingIds.Contains(record.SourceRecordId)))
        {
            var categories = _classifier.Classify(record.PermitType, record.PermitSubtype, record.Description);
            _db.PermitProjects.Add(new PermitProject
            {
                Id = record.Id,
                PermitSourceId = source.Id,
                Source = sourceId,
                SourceRecordId = record.SourceRecordId,
                Municipality = record.Municipality,
                State = record.State,
                Address = record.Address,
                NormalizedAddress = _addresses.Normalize(record.Address),
                PermitNumber = record.PermitNumber,
                PermitType = record.PermitType,
                PermitSubtype = record.PermitSubtype,
                Description = record.Description,
                Status = "Active",
                ApplicationDate = record.IssueDate.AddDays(-21),
                IssueDate = record.IssueDate,
                ExpirationDate = record.IssueDate.AddYears(1),
                EstimatedProjectValue = record.Value,
                ContractorName = record.Contractor,
                ResidentialOrCommercial = record.Occupancy,
                WorkCategory = categories[0],
                RawSourceUrl = source.OfficialDatasetUrl,
                LastSourceUpdate = record.IssueDate,
                CreatedAt = new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc),
                Categories = categories.Select(category => new PermitProjectCategory { Category = category }).ToList()
            });
        }
        await _db.SaveChangesAsync(ct);
    }

    private static McpTool Tool(Guid id, string name, string slug, string description, int price, Guid? projectId)
        => new()
        {
            Id = id, ProjectId = projectId, Name = name, Slug = slug, Description = description,
            Category = "construction-intelligence", Status = "Active", Visibility = "Unlisted",
            DefaultCostSats = Math.Max(1, price), MinCostSats = Math.Max(1, price), MaxCostSats = Math.Max(1, price),
            DocsUrl = "https://github.com/dulzuradev/LiveAuth/blob/master/docs/PermitSignal.md",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };

    private static IReadOnlyList<DemoRecord> DemoRecords() =>
    [
        new(Guid.Parse("20000000-0000-0000-0000-000000000001"), "demo-aus-hvac-001", "Austin", "TX", "500 CONGRESS AVE", "DEMO-2026-HVAC-001", "Mechanical Permit", "Commercial Remodel", "Replace three rooftop HVAC units and install building controls", new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc), 480_000, "Hill Country Mechanical", "Commercial"),
        new(Guid.Parse("20000000-0000-0000-0000-000000000002"), "demo-aus-roof-001", "Austin", "TX", "7429 PULLMAN CV", "DEMO-2026-ROOF-001", "Building Permit", "Residential Repair", "Reroof single-family residence with composition shingles", new DateTime(2026, 8, 4, 0, 0, 0, DateTimeKind.Utc), 28_000, "Lone Star Roofing", "Residential"),
        new(Guid.Parse("20000000-0000-0000-0000-000000000003"), "demo-aus-elec-001", "Austin", "TX", "901 E 6TH ST", "DEMO-2026-ELEC-001", "Electrical Permit", "Commercial Upgrade", "Upgrade electrical service from 200A to 600A with new switchgear", new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc), 420_000, "Central Texas Electric", "Commercial"),
        new(Guid.Parse("20000000-0000-0000-0000-000000000004"), "demo-sea-new-001", "Seattle", "WA", "1200 2ND AVE", "DEMO-2026-NEW-001", "Building", "New", "Construct new six-story commercial office building with ground-floor retail", new DateTime(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc), 12_500_000, "Cascadia Builders", "Commercial"),
        new(Guid.Parse("20000000-0000-0000-0000-000000000005"), "demo-sf-plumb-001", "San Francisco", "CA", "760 14TH ST", "DEMO-2026-PLUMB-001", "Building Permit", "Renovation", "Commercial renovation including plumbing fixtures, water lines, and accessible restrooms", new DateTime(2026, 8, 2, 0, 0, 0, DateTimeKind.Utc), 275_000, "Bay Plumbing Group", "Commercial")
    ];

    private sealed record DemoRecord(Guid Id, string SourceRecordId, string Municipality, string State,
        string Address, string PermitNumber, string PermitType, string PermitSubtype, string Description,
        DateTime IssueDate, decimal Value, string Contractor, string Occupancy);
}
