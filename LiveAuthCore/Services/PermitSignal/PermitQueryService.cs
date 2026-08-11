using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities.PermitSignal;
using LiveAuthCore.Models.PermitSignal;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Services.PermitSignal;

public interface IPermitQueryService
{
    Task<SearchProjectsResponse> SearchAsync(SearchProjectsRequest request, CancellationToken ct);
    Task<FindOpportunitiesResponse> FindOpportunitiesAsync(FindOpportunitiesRequest request, CancellationToken ct);
    Task<ProjectAnalysis?> AnalyzeProjectAsync(AnalyzeProjectRequest request, CancellationToken ct);
    Task<PropertyHistoryResponse> PropertyHistoryAsync(PropertyHistoryRequest request, CancellationToken ct);
}

public sealed class PermitQueryService : IPermitQueryService
{
    private readonly LiveAuthDbContext _db;
    private readonly IAddressNormalizer _addresses;
    private readonly IOpportunityScoringService _scoring;

    public PermitQueryService(LiveAuthDbContext db, IAddressNormalizer addresses, IOpportunityScoringService scoring)
    {
        _db = db;
        _addresses = addresses;
        _scoring = scoring;
    }

    public async Task<SearchProjectsResponse> SearchAsync(SearchProjectsRequest request, CancellationToken ct)
    {
        ValidateSearch(request);
        var query = ApplyFilters(_db.PermitProjects.AsNoTracking(), request)
            .Include(project => project.Categories)
            .OrderByDescending(project => project.IssueDate)
            .ThenByDescending(project => (double?)project.EstimatedProjectValue)
            .Take(Math.Clamp(request.Limit, 1, 100));
        var projects = await query.ToListAsync(ct);
        return new SearchProjectsResponse(projects.Count, projects.Select(PermitProjectDto.FromEntity).ToArray());
    }

    public async Task<FindOpportunitiesResponse> FindOpportunitiesAsync(FindOpportunitiesRequest request, CancellationToken ct)
    {
        if (TradeCategoryNormalizer.Normalize(request.Trade) == null)
            throw new PermitSignalValidationException($"Unsupported trade '{request.Trade}'.");
        if (request.IssuedWithinDays is < 1 or > 3650)
            throw new PermitSignalValidationException("issued_within_days must be between 1 and 3650.");

        var candidatesRequest = new SearchProjectsRequest
        {
            Location = request.Location,
            State = request.State,
            IssuedAfter = DateTime.UtcNow.Date.AddDays(-request.IssuedWithinDays),
            MinimumProjectValue = request.MinimumProjectValue,
            CommercialOnly = request.CommercialOnly,
            Limit = Math.Min(100, Math.Max(request.Limit * 4, 25))
        };

        var query = ApplyFilters(_db.PermitProjects.AsNoTracking(), candidatesRequest)
            .Include(project => project.Categories)
            .OrderByDescending(project => project.IssueDate)
            .Take(candidatesRequest.Limit);
        var projects = await query.ToListAsync(ct);

        var opportunities = projects
            .Select(project => (Project: project, Score: _scoring.Score(project, request.Trade)))
            .Where(item => item.Score.MatchStrength != "None")
            .OrderByDescending(item => item.Score.Score)
            .ThenByDescending(item => item.Project.IssueDate)
            .Take(Math.Clamp(request.Limit, 1, 100))
            .Select(item =>
            {
                var dto = PermitProjectDto.FromEntity(item.Project);
                return new PermitOpportunity(dto, item.Score.Score, item.Score.Level, item.Score.MatchedTrade,
                    item.Score.Reasons, item.Project.EstimatedProjectValue, PermitAgeDays(item.Project.IssueDate),
                    dto.Categories, dto.Source);
            })
            .ToArray();

        return new FindOpportunitiesResponse(opportunities.Length, opportunities);
    }

    public async Task<ProjectAnalysis?> AnalyzeProjectAsync(AnalyzeProjectRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectId))
            throw new PermitSignalValidationException("project_id is required.");

        var identifier = request.ProjectId.Trim();
        var query = _db.PermitProjects.AsNoTracking().Include(project => project.Categories).AsQueryable();
        PermitProject? project;
        if (Guid.TryParse(identifier, out var id))
            project = await query.SingleOrDefaultAsync(item => item.Id == id, ct);
        else
            project = await query.FirstOrDefaultAsync(item => item.SourceRecordId == identifier || item.PermitNumber == identifier, ct);
        if (project == null) return null;

        var dto = PermitProjectDto.FromEntity(project);
        var trades = dto.Categories.Where(IsTradeCategory).ToArray();
        var opportunities = trades.Select(TradeOpportunity).Distinct().ToArray();
        var signals = new List<string>();
        if (project.IssueDate.HasValue) signals.Add($"Permit issued {PermitAgeDays(project.IssueDate)} days ago");
        if (project.EstimatedProjectValue.HasValue) signals.Add($"Declared value: {project.EstimatedProjectValue.Value:0.##}");
        if (!string.IsNullOrWhiteSpace(project.ContractorName)) signals.Add("A contractor is identified in the public source record");
        if (string.Equals(project.ResidentialOrCommercial, "Commercial", StringComparison.OrdinalIgnoreCase)) signals.Add("Commercial project");

        var summary = $"{project.PermitType ?? "Construction permit"} at {project.Address}" +
                      (project.IssueDate.HasValue ? $", issued {project.IssueDate.Value:yyyy-MM-dd}" : string.Empty) + ".";
        return new ProjectAnalysis(dto, summary, project.Description, ProjectStage(project),
            PermitAgeDays(project.IssueDate), trades, opportunities, signals, [dto.Source]);
    }

    public async Task<PropertyHistoryResponse> PropertyHistoryAsync(PropertyHistoryRequest request, CancellationToken ct)
    {
        var normalized = _addresses.Normalize(request.Address);
        if (normalized.Length < 5)
            throw new PermitSignalValidationException("A complete street address is required.");

        var query = _db.PermitProjects.AsNoTracking()
            .Where(project => project.NormalizedAddress == normalized);
        if (!string.IsNullOrWhiteSpace(request.Municipality))
            query = query.Where(project => project.Municipality == request.Municipality.Trim());
        if (!string.IsNullOrWhiteSpace(request.State))
            query = query.Where(project => project.State == request.State.Trim().ToUpper());

        var summary = await query.Select(project => new
        {
            project.Id, project.IssueDate, project.ApplicationDate, project.EstimatedProjectValue
        }).ToListAsync(ct);
        var returned = await query.Include(project => project.Categories)
            .OrderBy(project => project.IssueDate)
            .Take(Math.Clamp(request.Limit, 1, 100)).ToListAsync(ct);
        var projectIds = summary.Select(item => item.Id).ToArray();
        var allCategories = await _db.PermitProjectCategories.AsNoTracking()
            .Where(category => projectIds.Contains(category.PermitProjectId))
            .Select(category => category.Category).ToListAsync(ct);
        var categories = allCategories
            .GroupBy(category => category, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count()).ThenBy(group => group.Key)
            .Select(group => group.Key).Take(8).ToArray();
        var majorIds = summary.Where(project => project.EstimatedProjectValue.HasValue)
            .OrderByDescending(project => project.EstimatedProjectValue).Take(5).Select(project => project.Id).ToArray();
        var majorEntities = await query.Include(project => project.Categories)
            .Where(project => majorIds.Contains(project.Id)).ToListAsync(ct);
        var majorById = majorEntities.ToDictionary(project => project.Id);
        var major = majorIds.Where(majorById.ContainsKey).Select(id => PermitProjectDto.FromEntity(majorById[id])).ToArray();

        return new PropertyHistoryResponse(request.Address, normalized, "ExactNormalizedAddress",
            summary.Count, summary.Select(project => project.IssueDate ?? project.ApplicationDate).Min(),
            summary.Select(project => project.IssueDate ?? project.ApplicationDate).Max(),
            summary.Sum(project => project.EstimatedProjectValue ?? 0), categories, major,
            returned.Select(PermitProjectDto.FromEntity).ToArray());
    }

    private static IQueryable<PermitProject> ApplyFilters(IQueryable<PermitProject> query, SearchProjectsRequest request)
    {
        var (municipality, locationState) = ParseLocation(request.Location);
        municipality ??= request.Municipality?.Trim();
        var state = (request.State ?? locationState)?.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(municipality))
        {
            var municipalityLower = municipality.ToLowerInvariant();
            query = query.Where(project => project.Municipality.ToLower() == municipalityLower);
        }
        if (!string.IsNullOrWhiteSpace(state)) query = query.Where(project => project.State == state);
        if (request.IssuedAfter.HasValue) query = query.Where(project => project.IssueDate >= request.IssuedAfter.Value);
        if (request.IssuedBefore.HasValue)
        {
            var issuedBefore = request.IssuedBefore.Value;
            query = issuedBefore.TimeOfDay == TimeSpan.Zero
                ? query.Where(project => project.IssueDate < issuedBefore.Date.AddDays(1))
                : query.Where(project => project.IssueDate <= issuedBefore);
        }
        if (request.MinimumProjectValue.HasValue) query = query.Where(project => project.EstimatedProjectValue >= request.MinimumProjectValue.Value);
        if (request.MaximumProjectValue.HasValue) query = query.Where(project => project.EstimatedProjectValue <= request.MaximumProjectValue.Value);
        if (!string.IsNullOrWhiteSpace(request.PermitType))
        {
            var pattern = $"%{EscapeLike(request.PermitType.Trim())}%";
            query = query.Where(project => (project.PermitType != null && EF.Functions.Like(project.PermitType, pattern)) ||
                                           (project.PermitSubtype != null && EF.Functions.Like(project.PermitSubtype, pattern)));
        }
        if (!string.IsNullOrWhiteSpace(request.WorkCategory))
        {
            var category = PermitWorkCategories.Normalize(request.WorkCategory)
                ?? throw new PermitSignalValidationException($"Unknown work_category '{request.WorkCategory}'.");
            query = query.Where(project => project.Categories.Any(item => item.Category == category));
        }
        if (request.CommercialOnly) query = query.Where(project => project.ResidentialOrCommercial == "Commercial");
        if (request.ResidentialOnly) query = query.Where(project => project.ResidentialOrCommercial == "Residential");
        if (!string.IsNullOrWhiteSpace(request.ContractorName))
        {
            var contractor = $"%{EscapeLike(request.ContractorName.Trim())}%";
            query = query.Where(project => project.ContractorName != null && EF.Functions.Like(project.ContractorName, contractor));
        }
        foreach (var keyword in (request.Keywords ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(5))
        {
            var pattern = $"%{EscapeLike(keyword)}%";
            query = query.Where(project => project.Description != null && EF.Functions.Like(project.Description, pattern));
        }
        return query;
    }

    private static void ValidateSearch(SearchProjectsRequest request)
    {
        if (request.CommercialOnly && request.ResidentialOnly)
            throw new PermitSignalValidationException("commercial_only and residential_only cannot both be true.");
        if (request.IssuedAfter > request.IssuedBefore)
            throw new PermitSignalValidationException("issued_after cannot be later than issued_before.");
        if (request.MinimumProjectValue > request.MaximumProjectValue)
            throw new PermitSignalValidationException("minimum_project_value cannot exceed maximum_project_value.");
        if (request.Limit is < 1 or > 100)
            throw new PermitSignalValidationException("limit must be between 1 and 100.");
    }

    private static (string? Municipality, string? State) ParseLocation(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return (null, null);
        var parts = location.Split(',', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 ? (parts[0], parts[1]) : (parts[0], null);
    }

    private static string EscapeLike(string value) => value.Replace("%", "[%]").Replace("_", "[_]");
    private static int? PermitAgeDays(DateTime? issueDate) => issueDate.HasValue ? Math.Max(0, (int)(DateTime.UtcNow.Date - issueDate.Value.Date).TotalDays) : null;
    private static bool IsTradeCategory(string category) => category is PermitWorkCategories.Hvac or PermitWorkCategories.Electrical or
        PermitWorkCategories.Plumbing or PermitWorkCategories.Roofing or PermitWorkCategories.Solar or PermitWorkCategories.FireProtection or
        PermitWorkCategories.Mechanical or PermitWorkCategories.Structural or PermitWorkCategories.Demolition or PermitWorkCategories.GeneralConstruction;
    private static string TradeOpportunity(string category) => category switch
    {
        PermitWorkCategories.Electrical => "Electrical contractor, switchgear, lighting, controls, and electrical-supply sales",
        PermitWorkCategories.Hvac or PermitWorkCategories.Mechanical => "HVAC/mechanical contractor, equipment, controls, and service sales",
        PermitWorkCategories.Plumbing => "Plumbing contractor, fixtures, piping, and water-system supply sales",
        PermitWorkCategories.Roofing => "Roofing contractor, membrane, insulation, and roof-accessory sales",
        PermitWorkCategories.FireProtection => "Fire alarm, sprinkler, suppression, and inspection services",
        PermitWorkCategories.Solar => "Solar installer, inverter, racking, storage, and electrical sales",
        PermitWorkCategories.Structural => "Structural engineering, concrete, steel, and reinforcement services",
        PermitWorkCategories.Demolition => "Demolition, hauling, abatement, and site-preparation services",
        _ => "General contracting, material supply, equipment rental, and construction services"
    };
    private static string ProjectStage(PermitProject project)
    {
        if (!string.IsNullOrWhiteSpace(project.Status) && project.Status.Contains("complete", StringComparison.OrdinalIgnoreCase)) return "Completed";
        if (project.IssueDate.HasValue) return "Permitted";
        if (project.ApplicationDate.HasValue) return "Applied";
        return "Unknown";
    }
}
