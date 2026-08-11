using System.Globalization;
using System.Net;
using System.Text.Json;

namespace LiveAuthCore.Services.PermitSignal;

public sealed record PermitFetchRequest(DateTime SinceUtc, int Offset, int PageSize);

public sealed record PermitSourcePage(IReadOnlyList<NormalizedPermitRecord> Records,
    int? NextOffset, DateTime? MaximumSourceUpdate);

public sealed record NormalizedPermitRecord(
    string Source,
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
    string? RawSourceUrl,
    DateTime? LastSourceUpdate);

public interface IPermitSourceAdapter
{
    string SourceIdentifier { get; }
    string Municipality { get; }
    string State { get; }
    string AdapterType { get; }
    string OfficialDatasetUrl { get; }
    Task<PermitSourcePage> FetchAsync(PermitFetchRequest request, CancellationToken ct);
}

public abstract class SocrataPermitAdapter : IPermitSourceAdapter
{
    private readonly HttpClient _http;
    private readonly Uri _resourceUri;
    private readonly string _incrementalField;

    protected SocrataPermitAdapter(HttpClient http, string resourceUrl, string incrementalField)
    {
        _http = http;
        _resourceUri = new Uri(resourceUrl, UriKind.Absolute);
        _incrementalField = incrementalField;
    }

    public abstract string SourceIdentifier { get; }
    public abstract string Municipality { get; }
    public abstract string State { get; }
    public string AdapterType => GetType().Name;
    public abstract string OfficialDatasetUrl { get; }

    public async Task<PermitSourcePage> FetchAsync(PermitFetchRequest request, CancellationToken ct)
    {
        var pageSize = Math.Clamp(request.PageSize, 1, 1000);
        var offset = Math.Max(0, request.Offset);
        var since = request.SinceUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var query = $"$limit={pageSize}&$offset={offset}&$order={_incrementalField} ASC&$where={_incrementalField} >= '{since}'";
        var uri = new UriBuilder(_resourceUri) { Query = query }.Uri;
        using var document = await GetJsonWithRetryAsync(uri, ct);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new PermitSourceException(SourceIdentifier, "The source returned a non-array response.");

        var records = new List<NormalizedPermitRecord>();
        foreach (var row in document.RootElement.EnumerateArray())
        {
            var mapped = Map(row);
            if (mapped != null && !string.IsNullOrWhiteSpace(mapped.SourceRecordId) && !string.IsNullOrWhiteSpace(mapped.Address))
                records.Add(mapped);
        }

        var maxUpdate = records.Select(record => record.LastSourceUpdate).Max();
        return new PermitSourcePage(records, document.RootElement.GetArrayLength() == pageSize ? offset + pageSize : null, maxUpdate);
    }

    protected abstract NormalizedPermitRecord? Map(JsonElement row);

    private async Task<JsonDocument> GetJsonWithRetryAsync(Uri uri, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
                if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                {
                    if (attempt == 3) response.EnsureSuccessStatusCode();
                    var retry = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMilliseconds(250 * attempt * attempt);
                    await Task.Delay(retry > TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(5) : retry, ct);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            }
            catch (HttpRequestException) when (attempt < 3)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt * attempt), ct);
            }
        }

        throw new PermitSourceException(SourceIdentifier, "The source could not be reached after three attempts.");
    }

    protected static string? Text(JsonElement row, string field)
    {
        if (!row.TryGetProperty(field, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => NullIfBlank(value.GetString()),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    protected static DateTime? Date(JsonElement row, params string[] fields)
    {
        foreach (var field in fields)
        {
            var value = Text(row, field);
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        }
        return null;
    }

    protected static decimal? Decimal(JsonElement row, params string[] fields)
    {
        foreach (var field in fields)
        {
            var value = Text(row, field);
            if (decimal.TryParse(value, NumberStyles.Number | NumberStyles.AllowExponent,
                    CultureInfo.InvariantCulture, out var parsed))
                return parsed;
        }
        return null;
    }

    protected static string? Link(JsonElement row)
    {
        if (!row.TryGetProperty("link", out var link)) return null;
        if (link.ValueKind == JsonValueKind.String) return NullIfBlank(link.GetString());
        return link.ValueKind == JsonValueKind.Object && link.TryGetProperty("url", out var url)
            ? NullIfBlank(url.GetString())
            : null;
    }

    protected static string JoinAddress(params string?[] parts)
        => string.Join(' ', parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim()));

    protected static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class AustinPermitAdapter : SocrataPermitAdapter
{
    public const string ResourceUrl = "https://data.austintexas.gov/resource/3syk-w9eu.json";
    public AustinPermitAdapter(HttpClient http) : base(http, ResourceUrl, "statusdate") { }
    public override string SourceIdentifier => "austin-issued-construction-permits";
    public override string Municipality => "Austin";
    public override string State => "TX";
    public override string OfficialDatasetUrl => "https://data.austintexas.gov/Building-and-Development/Issued-Construction-Permits/3syk-w9eu";

    protected override NormalizedPermitRecord? Map(JsonElement row)
    {
        var id = Text(row, "project_id") ?? Text(row, "permit_number");
        if (id == null) return null;
        var address = Text(row, "original_address1") ?? Text(row, "permit_location") ?? string.Empty;
        return new NormalizedPermitRecord(SourceIdentifier, id, Municipality, State, address,
            Decimal(row, "latitude"), Decimal(row, "longitude"), Text(row, "permit_number") ?? id,
            Text(row, "permit_type_desc") ?? Text(row, "permittype"), Text(row, "work_class"),
            Text(row, "description"), Text(row, "status_current"), Date(row, "applieddate"),
            Date(row, "issue_date"), Date(row, "expiresdate"),
            Decimal(row, "total_job_valuation", "building_valuation", "total_valuation_remodel"),
            Text(row, "contractor_company_name") ?? Text(row, "contractor_full_name"), null, null,
            NormalizeOccupancy(Text(row, "permit_class_mapped")), Link(row),
            Date(row, "statusdate", "issue_date"));
    }

    private static string? NormalizeOccupancy(string? value)
        => value?.Contains("residential", StringComparison.OrdinalIgnoreCase) == true ? "Residential" :
           value?.Contains("commercial", StringComparison.OrdinalIgnoreCase) == true ? "Commercial" : null;
}

public sealed class SanFranciscoPermitAdapter : SocrataPermitAdapter
{
    public const string ResourceUrl = "https://data.sfgov.org/resource/i98e-djp9.json";
    public SanFranciscoPermitAdapter(HttpClient http) : base(http, ResourceUrl, "data_loaded_at") { }
    public override string SourceIdentifier => "san-francisco-building-permits";
    public override string Municipality => "San Francisco";
    public override string State => "CA";
    public override string OfficialDatasetUrl => "https://data.sfgov.org/Housing-and-Buildings/Building-Permits/i98e-djp9";

    protected override NormalizedPermitRecord? Map(JsonElement row)
    {
        var id = Text(row, "record_id") ?? Text(row, "permit_number");
        if (id == null) return null;
        var address = JoinAddress(Text(row, "street_number"), Text(row, "street_number_suffix"),
            Text(row, "street_name"), Text(row, "street_suffix"), Unit(row));
        var (latitude, longitude) = Point(row);
        var use = Text(row, "proposed_use") ?? Text(row, "existing_use");
        return new NormalizedPermitRecord(SourceIdentifier, id, Municipality, State, address,
            latitude, longitude, Text(row, "permit_number") ?? id, Text(row, "permit_type_definition"),
            Text(row, "permit_type"), Text(row, "description"), Text(row, "status"),
            Date(row, "filed_date", "permit_creation_date"), Date(row, "issued_date"), null,
            Decimal(row, "revised_cost", "estimated_cost"), null, null, null, Occupancy(use),
            $"https://data.sfgov.org/resource/i98e-djp9.json?record_id={Uri.EscapeDataString(id)}",
            Date(row, "data_loaded_at", "data_as_of", "last_permit_activity_date"));
    }

    private static string? Unit(JsonElement row)
    {
        var unit = Text(row, "unit");
        return unit == null ? null : $"UNIT {unit}{Text(row, "unit_suffix")}";
    }

    private static (decimal? Latitude, decimal? Longitude) Point(JsonElement row)
    {
        if (!row.TryGetProperty("location", out var location) || location.ValueKind != JsonValueKind.Object ||
            !location.TryGetProperty("coordinates", out var coordinates) || coordinates.GetArrayLength() < 2)
            return (null, null);
        var values = coordinates.EnumerateArray().ToArray();
        return (values[1].TryGetDecimal(out var latitude) ? latitude : null,
            values[0].TryGetDecimal(out var longitude) ? longitude : null);
    }

    private static string? Occupancy(string? use)
    {
        if (string.IsNullOrWhiteSpace(use)) return null;
        var residentialTerms = new[] { "family", "apartment", "dwelling", "residence", "residential", "hotel", "condo" };
        return residentialTerms.Any(term => use.Contains(term, StringComparison.OrdinalIgnoreCase))
            ? "Residential"
            : "Commercial";
    }
}

public sealed class SeattlePermitAdapter : SocrataPermitAdapter
{
    public const string ResourceUrl = "https://data.seattle.gov/resource/76t5-zqzr.json";
    public SeattlePermitAdapter(HttpClient http) : base(http, ResourceUrl, "issueddate") { }
    public override string SourceIdentifier => "seattle-building-permits";
    public override string Municipality => "Seattle";
    public override string State => "WA";
    public override string OfficialDatasetUrl => "https://data.seattle.gov/Permitting/Building-Permits/76t5-zqzr";

    protected override NormalizedPermitRecord? Map(JsonElement row)
    {
        var id = Text(row, "permitnum");
        if (id == null) return null;
        var mapped = Text(row, "permitclassmapped");
        var occupancy = mapped?.Equals("Residential", StringComparison.OrdinalIgnoreCase) == true ? "Residential" :
            mapped?.Equals("Non-Residential", StringComparison.OrdinalIgnoreCase) == true ? "Commercial" : null;
        return new NormalizedPermitRecord(SourceIdentifier, id, Municipality, State,
            Text(row, "originaladdress1") ?? string.Empty, Decimal(row, "latitude"), Decimal(row, "longitude"),
            id, Text(row, "permittypemapped"), Text(row, "permittypedesc"), Text(row, "description"),
            Text(row, "statuscurrent"), Date(row, "applieddate"), Date(row, "issueddate"),
            Date(row, "expiresdate"), Decimal(row, "estprojectcost"), Text(row, "contractorcompanyname"),
            null, null, occupancy, Link(row),
            Date(row, "issueddate"));
    }
}

public sealed class PermitSourceException : Exception
{
    public PermitSourceException(string source, string message) : base($"{source}: {message}")
        => SourceIdentifier = source;
    public string SourceIdentifier { get; }
}
