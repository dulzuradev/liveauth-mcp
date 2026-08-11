using LiveAuthCore.Extensions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LiveAuthCore.Tests.Bitcoin;

public sealed class BitcoinGatewayPersistenceTests
{
    [Fact]
    public async Task Durable_operation_schema_is_idempotent_and_indexes_replay_keys()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await PipelineExtensions.RunTableMigrationsAsync(connection);
        await PipelineExtensions.RunTableMigrationsAsync(connection);

        await using var columns = connection.CreateCommand();
        columns.CommandText = "SELECT GROUP_CONCAT(name, ',') FROM pragma_table_info('BitcoinGatewayOperations')";
        var columnNames = (string?)await columns.ExecuteScalarAsync() ?? string.Empty;
        Assert.Contains("RequestHash", columnNames);
        Assert.Contains("RequestId", columnNames);
        Assert.Contains("RevenueEventId", columnNames);
        Assert.DoesNotContain("RawTransaction", columnNames, StringComparison.OrdinalIgnoreCase);

        await using var indexes = connection.CreateCommand();
        indexes.CommandText = "SELECT GROUP_CONCAT(name, ',') FROM pragma_index_list('BitcoinGatewayOperations')";
        var indexNames = (string?)await indexes.ExecuteScalarAsync() ?? string.Empty;
        Assert.Contains("IX_BitcoinGatewayOperations_ProjectId_Operation_IdempotencyKey", indexNames);
        Assert.Contains("IX_BitcoinGatewayOperations_RevenueEventId", indexNames);
    }
}
