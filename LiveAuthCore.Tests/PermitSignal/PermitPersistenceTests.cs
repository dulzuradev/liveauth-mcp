using LiveAuthCore.Data.Entities.PermitSignal;
using LiveAuthCore.Models.PermitSignal;
using LiveAuthCore.Services.PermitSignal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LiveAuthCore.Tests.PermitSignal;

public sealed class PermitPersistenceTests
{
    [Fact]
    public async Task PermitSignal_table_migration_is_idempotent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await LiveAuthCore.Extensions.PipelineExtensions.RunTableMigrationsAsync(connection);
        await LiveAuthCore.Extensions.PipelineExtensions.RunTableMigrationsAsync(connection);

        foreach (var table in new[] { "PermitSources", "PermitSyncStates", "PermitProjects", "PermitProjectCategories" })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name";
            command.Parameters.AddWithValue("$name", table);
            Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
        }
    }

    [Fact]
    public async Task Repeated_synchronization_updates_without_duplication()
    {
        await using var fixture = await PermitSignalTestFixture.CreateAsync();
        var adapter = new FakeAdapter();
        var options = new PermitSignalOptions { Sync = new PermitSignalSyncOptions { PageSize = 50, MaximumPagesPerSource = 2 } };
        var service = new PermitSynchronizationService(fixture.Db, [adapter], new PermitCategoryClassifier(),
            new AddressNormalizer(), PermitSignalTestFixture.Options(options), NullLogger<PermitSynchronizationService>.Instance);

        var first = Assert.Single(await service.SynchronizeAsync(null, default));
        adapter.Value = 450_000;
        var second = Assert.Single(await service.SynchronizeAsync(null, default));

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(1, await fixture.Db.PermitProjects.CountAsync());
        Assert.Equal(450_000m, (await fixture.Db.PermitProjects.SingleAsync()).EstimatedProjectValue);
        Assert.Equal(1, second.Updated);
    }

    [Fact]
    public async Task Search_filters_by_city_value_category_and_occupancy()
    {
        await using var fixture = await PermitSignalTestFixture.CreateAsync();
        var source = new PermitSource { SourceIdentifier = "test", Municipality = "Austin", State = "TX", AdapterType = "test", OfficialDatasetUrl = "https://example.invalid" };
        fixture.Db.PermitSources.Add(source);
        fixture.Db.PermitProjects.AddRange(
            Project(source, "one", "Austin", "Commercial", 500_000, PermitWorkCategories.Electrical),
            Project(source, "two", "Austin", "Residential", 600_000, PermitWorkCategories.Electrical),
            Project(source, "three", "Seattle", "Commercial", 900_000, PermitWorkCategories.Hvac));
        await fixture.Db.SaveChangesAsync();
        var query = new PermitQueryService(fixture.Db, new AddressNormalizer(),
            new OpportunityScoringService(PermitSignalTestFixture.Options()));

        var result = await query.SearchAsync(new SearchProjectsRequest
        {
            Location = "Austin, TX", MinimumProjectValue = 250_000,
            WorkCategory = "Electrical", CommercialOnly = true, Limit = 25
        }, default);

        Assert.Equal(1, result.Count);
        Assert.Equal("one", result.Projects[0].SourceRecordId);
        Assert.Equal("test", result.Projects[0].Source.Identifier);
    }

    [Fact]
    public async Task Property_history_requires_exact_normalized_address()
    {
        await using var fixture = await PermitSignalTestFixture.CreateAsync();
        var source = new PermitSource { SourceIdentifier = "test", Municipality = "San Francisco", State = "CA", AdapterType = "test", OfficialDatasetUrl = "https://example.invalid" };
        fixture.Db.PermitSources.Add(source);
        var first = Project(source, "one", "San Francisco", "Commercial", 100_000, PermitWorkCategories.Renovation);
        first.Address = "760 14TH ST UNIT 2";
        first.NormalizedAddress = new AddressNormalizer().Normalize(first.Address);
        var second = Project(source, "two", "San Francisco", "Commercial", 250_000, PermitWorkCategories.Plumbing);
        second.Address = "760 14th Street, Apt 2";
        second.NormalizedAddress = new AddressNormalizer().Normalize(second.Address);
        fixture.Db.PermitProjects.AddRange(first, second);
        await fixture.Db.SaveChangesAsync();
        var query = new PermitQueryService(fixture.Db, new AddressNormalizer(),
            new OpportunityScoringService(PermitSignalTestFixture.Options()));

        var result = await query.PropertyHistoryAsync(new PropertyHistoryRequest { Address = "760 14th St #2", Limit = 1 }, default);
        Assert.Equal(2, result.TotalPermits);
        Assert.Equal(350_000m, result.TotalKnownPermittedValue);
        Assert.Single(result.Permits);
        Assert.Equal("ExactNormalizedAddress", result.MatchConfidence);
    }

    private static PermitProject Project(PermitSource source, string id, string municipality,
        string occupancy, decimal value, string category) => new()
    {
        PermitSource = source, Source = source.SourceIdentifier, SourceRecordId = id,
        Municipality = municipality, State = municipality == "Seattle" ? "WA" : "TX",
        Address = $"{id} MAIN ST", NormalizedAddress = $"{id.ToUpper()} MAIN ST", PermitNumber = id,
        IssueDate = DateTime.UtcNow.AddDays(-2), EstimatedProjectValue = value,
        ResidentialOrCommercial = occupancy, WorkCategory = category,
        Categories = [new PermitProjectCategory { Category = category }]
    };

    private sealed class FakeAdapter : IPermitSourceAdapter
    {
        public decimal Value { get; set; } = 300_000;
        public string SourceIdentifier => "fake-source";
        public string Municipality => "Austin";
        public string State => "TX";
        public string AdapterType => "Fake";
        public string OfficialDatasetUrl => "https://example.invalid/permits";
        public Task<PermitSourcePage> FetchAsync(PermitFetchRequest request, CancellationToken ct)
        {
            var updated = new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc);
            NormalizedPermitRecord record = new(SourceIdentifier, "stable-record", Municipality, State,
                "901 E 6TH ST", null, null, "PERMIT-1", "Electrical Permit", "Commercial Upgrade",
                "Electrical service upgrade", "Issued", updated.AddDays(-10), updated, null, Value,
                "Test Electric", null, null, "Commercial", OfficialDatasetUrl, updated);
            return Task.FromResult(new PermitSourcePage([record], null, updated));
        }
    }
}
