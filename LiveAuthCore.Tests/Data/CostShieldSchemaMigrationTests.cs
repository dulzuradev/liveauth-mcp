using FluentAssertions;
using LiveAuthCore.Extensions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LiveAuthCore.Tests.Schema;

public sealed class CostShieldSchemaMigrationTests
{
    [Fact]
    public async Task RunTableMigrations_CreatesCostShieldSchemaAndIsIdempotent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await ExecuteAsync(
            connection,
            """
            CREATE TABLE Projects (
                Id TEXT NOT NULL PRIMARY KEY
            );
            """);

        await PipelineExtensions.RunTableMigrationsAsync(connection);
        await PipelineExtensions.RunTableMigrationsAsync(connection);

        var columns = await ReadNamesAsync(
            connection,
            "SELECT name FROM pragma_table_info('ProtectedActions') ORDER BY cid");
        columns.Should().Contain(new[]
        {
            "Id",
            "ProjectId",
            "Environment",
            "Name",
            "AllowedOriginsRaw",
            "ConfigurationVersion"
        });

        var indexes = await ReadNamesAsync(
            connection,
            "SELECT name FROM pragma_index_list('ProtectedActions')");
        indexes.Should().Contain("IX_ProtectedActions_ProjectId_Environment_Name");
        indexes.Should().Contain("IX_ProtectedActions_ProjectId_Environment_IsEnabled");

        var authorizationColumns = await ReadNamesAsync(
            connection,
            "SELECT name FROM pragma_table_info('CostShieldAuthorizations') ORDER BY cid");
        authorizationColumns.Should().Contain(new[]
        {
            "Id",
            "ProjectId",
            "ProtectedActionId",
            "ChallengeId",
            "TokenId",
            "Status",
            "ConcurrencyStamp"
        });

        var authorizationIndexes = await ReadNamesAsync(
            connection,
            "SELECT name FROM pragma_index_list('CostShieldAuthorizations')");
        authorizationIndexes.Should().Contain(
            "IX_CostShieldAuthorizations_TokenId");
        authorizationIndexes.Should().Contain(
            "IX_CostShieldAuthorizations_ProjectId_ChallengeId");
        authorizationIndexes.Should().Contain(
            "IX_CostShieldAuthorizations_ProjectId_ProtectedActionId_IssuedAt");

        var meterTables = await ReadNamesAsync(connection,
            "SELECT name FROM sqlite_master WHERE type='table' AND name LIKE 'Meter%' ORDER BY name");
        meterTables.Should().Contain(new[]
        {
            "MeterProjectSettings", "MeterRouteRules", "MeterPaymentChallenges",
            "MeterAllowanceCounters", "MeterUsageEvents", "MeterReceipts"
        });
        var meterIndexes = await ReadNamesAsync(connection,
            "SELECT name FROM sqlite_master WHERE type='index' AND name LIKE 'IX_Meter%' ORDER BY name");
        meterIndexes.Should().Contain(new[]
        {
            "IX_MeterProjectSettings_ProjectId",
            "IX_MeterPaymentChallenges_ChallengeKey",
            "IX_MeterAllowanceCounters_Unique",
            "IX_MeterReceipts_ChallengeId_RequestCorrelationId"
        });
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<string>> ReadNamesAsync(
        SqliteConnection connection,
        string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();

        var values = new List<string>();
        while (await reader.ReadAsync())
            values.Add(reader.GetString(0));

        return values;
    }
}
