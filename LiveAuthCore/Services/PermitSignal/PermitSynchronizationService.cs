using System.Diagnostics;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities.PermitSignal;
using LiveAuthCore.Models.PermitSignal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LiveAuthCore.Services.PermitSignal;

public sealed record PermitSyncResult(string Source, bool Success, int Added, int Updated,
    int Processed, TimeSpan Duration, string? Error = null);

public interface IPermitSynchronizationService
{
    Task<IReadOnlyList<PermitSyncResult>> SynchronizeAsync(string? sourceIdentifier, CancellationToken ct);
}

public sealed class PermitSynchronizationService : IPermitSynchronizationService
{
    private readonly LiveAuthDbContext _db;
    private readonly IReadOnlyList<IPermitSourceAdapter> _adapters;
    private readonly IPermitCategoryClassifier _classifier;
    private readonly IAddressNormalizer _addresses;
    private readonly PermitSignalSyncOptions _options;
    private readonly ILogger<PermitSynchronizationService> _logger;

    public PermitSynchronizationService(LiveAuthDbContext db, IEnumerable<IPermitSourceAdapter> adapters,
        IPermitCategoryClassifier classifier, IAddressNormalizer addresses,
        IOptions<PermitSignalOptions> options, ILogger<PermitSynchronizationService> logger)
    {
        _db = db;
        _adapters = adapters.ToArray();
        _classifier = classifier;
        _addresses = addresses;
        _options = options.Value.Sync;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PermitSyncResult>> SynchronizeAsync(string? sourceIdentifier, CancellationToken ct)
    {
        var adapters = string.IsNullOrWhiteSpace(sourceIdentifier)
            ? _adapters
            : _adapters.Where(adapter => adapter.SourceIdentifier.Equals(sourceIdentifier, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (adapters.Count == 0)
            throw new PermitSignalValidationException($"Unknown permit source '{sourceIdentifier}'.");

        var results = new List<PermitSyncResult>();
        foreach (var adapter in adapters)
            results.Add(await SynchronizeSourceAsync(adapter, ct));
        return results;
    }

    private async Task<PermitSyncResult> SynchronizeSourceAsync(IPermitSourceAdapter adapter, CancellationToken ct)
    {
        var timer = Stopwatch.StartNew();
        var source = await GetOrCreateSourceAsync(adapter, ct);
        var state = source.SyncState ?? new PermitSyncState { PermitSourceId = source.Id };
        if (source.SyncState == null)
        {
            _db.PermitSyncStates.Add(state);
            source.SyncState = state;
        }
        state.LastAttemptAt = DateTime.UtcNow;
        source.HealthStatus = "Syncing";
        source.LastError = null;
        await _db.SaveChangesAsync(ct);

        var added = 0;
        var updated = 0;
        var processed = 0;
        var offset = ParseOffset(state.ContinuationToken);
        var since = state.SourceCursorUtc ?? DateTime.UtcNow.AddDays(-Math.Clamp(_options.InitialLookbackDays, 1, 3650));
        DateTime? maximumUpdate = state.SourceCursorUtc;

        try
        {
            for (var pageNumber = 0; pageNumber < Math.Clamp(_options.MaximumPagesPerSource, 1, 100); pageNumber++)
            {
                var page = await adapter.FetchAsync(new PermitFetchRequest(since, offset,
                    Math.Clamp(_options.PageSize, 1, 1000)), ct);
                var counts = await UpsertPageAsync(source, page.Records, ct);
                added += counts.Added;
                updated += counts.Updated;
                processed += page.Records.Count;
                maximumUpdate = Max(maximumUpdate, page.MaximumSourceUpdate);
                state.RecordsProcessed += page.Records.Count;
                state.ContinuationToken = page.NextOffset?.ToString();
                state.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);

                if (!page.NextOffset.HasValue)
                {
                    state.ContinuationToken = null;
                    state.SourceCursorUtc = maximumUpdate ?? DateTime.UtcNow;
                    break;
                }
                offset = page.NextOffset.Value;
            }

            state.LastSuccessfulSyncAt = DateTime.UtcNow;
            state.ConsecutiveFailures = 0;
            state.LastError = null;
            source.LastSuccessfulSync = state.LastSuccessfulSyncAt;
            source.HealthStatus = "Healthy";
            source.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("PermitSignal synchronized {Source}: {Processed} processed, {Added} added, {Updated} updated in {DurationMs} ms",
                adapter.SourceIdentifier, processed, added, updated, timer.ElapsedMilliseconds);
            return new PermitSyncResult(adapter.SourceIdentifier, true, added, updated, processed, timer.Elapsed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            state.ConsecutiveFailures++;
            state.LastError = Truncate(ex.Message, 2000);
            state.UpdatedAt = DateTime.UtcNow;
            source.HealthStatus = "Unhealthy";
            source.LastError = state.LastError;
            source.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            _logger.LogWarning(ex, "PermitSignal synchronization failed for {Source} after {DurationMs} ms",
                adapter.SourceIdentifier, timer.ElapsedMilliseconds);
            return new PermitSyncResult(adapter.SourceIdentifier, false, added, updated, processed, timer.Elapsed, state.LastError);
        }
    }

    private async Task<(int Added, int Updated)> UpsertPageAsync(PermitSource source,
        IReadOnlyList<NormalizedPermitRecord> records, CancellationToken ct)
    {
        if (records.Count == 0) return (0, 0);
        var ids = records.Select(record => record.SourceRecordId).Distinct().ToArray();
        var existing = await _db.PermitProjects
            .Include(project => project.Categories)
            .Where(project => project.PermitSourceId == source.Id && ids.Contains(project.SourceRecordId))
            .ToDictionaryAsync(project => project.SourceRecordId, StringComparer.Ordinal, ct);
        var added = 0;
        var updated = 0;

        foreach (var record in records.GroupBy(item => item.SourceRecordId).Select(group => group.Last()))
        {
            if (!existing.TryGetValue(record.SourceRecordId, out var project))
            {
                project = new PermitProject
                {
                    PermitSourceId = source.Id,
                    Source = record.Source,
                    SourceRecordId = record.SourceRecordId,
                    CreatedAt = DateTime.UtcNow
                };
                _db.PermitProjects.Add(project);
                added++;
            }
            else
            {
                updated++;
            }

            Apply(project, record);
            var categories = _classifier.Classify(record.PermitType, record.PermitSubtype, record.Description);
            project.WorkCategory = categories.FirstOrDefault() ?? PermitWorkCategories.Other;
            var desired = categories.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var obsolete in project.Categories.Where(category => !desired.Contains(category.Category)).ToArray())
                project.Categories.Remove(obsolete);
            foreach (var category in desired.Where(category => project.Categories.All(item => !item.Category.Equals(category, StringComparison.OrdinalIgnoreCase))))
                project.Categories.Add(new PermitProjectCategory { PermitProject = project, Category = category });
        }

        await _db.SaveChangesAsync(ct);
        return (added, updated);
    }

    private void Apply(PermitProject project, NormalizedPermitRecord record)
    {
        project.Source = record.Source;
        project.Municipality = record.Municipality;
        project.State = record.State;
        project.Address = record.Address.Trim();
        project.NormalizedAddress = _addresses.Normalize(record.Address);
        project.Latitude = record.Latitude;
        project.Longitude = record.Longitude;
        project.PermitNumber = record.PermitNumber;
        project.PermitType = record.PermitType;
        project.PermitSubtype = record.PermitSubtype;
        project.Description = record.Description;
        project.Status = record.Status;
        project.ApplicationDate = record.ApplicationDate;
        project.IssueDate = record.IssueDate;
        project.ExpirationDate = record.ExpirationDate;
        project.EstimatedProjectValue = record.EstimatedProjectValue;
        project.ContractorName = record.ContractorName;
        project.ContractorLicense = record.ContractorLicense;
        project.OwnerName = record.OwnerName;
        project.ResidentialOrCommercial = record.ResidentialOrCommercial;
        project.RawSourceUrl = record.RawSourceUrl;
        project.LastSourceUpdate = record.LastSourceUpdate;
        project.UpdatedAt = DateTime.UtcNow;
    }

    private async Task<PermitSource> GetOrCreateSourceAsync(IPermitSourceAdapter adapter, CancellationToken ct)
    {
        var source = await _db.PermitSources.Include(item => item.SyncState)
            .SingleOrDefaultAsync(item => item.SourceIdentifier == adapter.SourceIdentifier, ct);
        if (source != null) return source;
        source = new PermitSource
        {
            SourceIdentifier = adapter.SourceIdentifier,
            Municipality = adapter.Municipality,
            State = adapter.State,
            AdapterType = adapter.AdapterType,
            OfficialDatasetUrl = adapter.OfficialDatasetUrl,
            HealthStatus = "Pending"
        };
        _db.PermitSources.Add(source);
        await _db.SaveChangesAsync(ct);
        return source;
    }

    private static int ParseOffset(string? value) => int.TryParse(value, out var offset) && offset > 0 ? offset : 0;
    private static DateTime? Max(DateTime? left, DateTime? right) => !left.HasValue ? right : !right.HasValue ? left : left > right ? left : right;
    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];
}

public sealed class PermitSynchronizationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PermitSignalSyncOptions _options;
    private readonly ILogger<PermitSynchronizationWorker> _logger;

    public PermitSynchronizationWorker(IServiceScopeFactory scopeFactory, IOptions<PermitSignalOptions> options,
        ILogger<PermitSynchronizationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value.Sync;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("PermitSignal automatic synchronization is disabled.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var synchronization = scope.ServiceProvider.GetRequiredService<IPermitSynchronizationService>();
                await synchronization.SynchronizeAsync(null, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PermitSignal synchronization cycle failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(Math.Clamp(_options.IntervalMinutes, 5, 1440)), stoppingToken);
        }
    }
}
