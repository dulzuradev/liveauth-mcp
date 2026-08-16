using LiveAuthCore.Extensions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LiveAuthCore.Tests.Schema;

public sealed class McpRevenueSchemaMigrationTests
{
    private const string ScopedIndexName =
        "IX_McpToolRevenueEvents_McpToolId_PayingProjectId_IdempotencyKey";
    private const string LegacyIndexName =
        "IX_McpToolRevenueEvents_McpToolId_IdempotencyKey";

    [Fact]
    public async Task RunTableMigrations_ReplacesGlobalIdempotencyIndex_WithProjectScopedIndex()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await PipelineExtensions.RunTableMigrationsAsync(connection);

        await ExecuteAsync(connection, $"DROP INDEX {ScopedIndexName}");
        await ExecuteAsync(connection, $"""
            CREATE UNIQUE INDEX {LegacyIndexName}
            ON McpToolRevenueEvents (McpToolId, IdempotencyKey)
            WHERE IdempotencyKey IS NOT NULL
            """);

        await PipelineExtensions.RunTableMigrationsAsync(connection);
        await PipelineExtensions.RunTableMigrationsAsync(connection);

        var indexes = await ReadNamesAsync(
            connection,
            "SELECT name FROM pragma_index_list('McpToolRevenueEvents')");
        Assert.Contains(ScopedIndexName, indexes);
        Assert.DoesNotContain(LegacyIndexName, indexes);

        var columns = await ReadNamesAsync(
            connection,
            $"SELECT name FROM pragma_index_info('{ScopedIndexName}') ORDER BY seqno");
        Assert.Equal(new[] { "McpToolId", "PayingProjectId", "IdempotencyKey" }, columns);

        var toolId = Guid.NewGuid();
        var firstProjectId = Guid.NewGuid();
        var secondProjectId = Guid.NewGuid();
        await InsertRevenueEventAsync(connection, toolId, firstProjectId, "shared-key");
        await InsertRevenueEventAsync(connection, toolId, secondProjectId, "shared-key");

        await Assert.ThrowsAsync<SqliteException>(() =>
            InsertRevenueEventAsync(connection, toolId, firstProjectId, "shared-key"));
    }

    private static async Task InsertRevenueEventAsync(
        SqliteConnection connection,
        Guid toolId,
        Guid projectId,
        string idempotencyKey)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO McpToolRevenueEvents (
                Id, McpToolId, PayingProjectId, ToolMethodName,
                GrossSats, PlatformFeeSats, NetSats, FeeBasisPoints,
                Status, IdempotencyKey, CreatedAt)
            VALUES (
                $id, $toolId, $projectId, 'anonymous-agent-call',
                1, 1, 0, 500,
                'Charged', $idempotencyKey, $createdAt)
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$toolId", toolId.ToString());
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        command.Parameters.AddWithValue("$idempotencyKey", idempotencyKey);
        command.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
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
        var names = new List<string>();
        while (await reader.ReadAsync())
            names.Add(reader.GetString(0));
        return names;
    }
}
