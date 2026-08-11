using LiveAuthCore.Data;
using LiveAuthCore.Models.PermitSignal;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LiveAuthCore.Tests.PermitSignal;

internal sealed class PermitSignalTestFixture : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    public LiveAuthDbContext Db { get; }

    private PermitSignalTestFixture(SqliteConnection connection, LiveAuthDbContext db)
    {
        _connection = connection;
        Db = db;
    }

    public static async Task<PermitSignalTestFixture> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var db = new LiveAuthDbContext(new DbContextOptionsBuilder<LiveAuthDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        return new PermitSignalTestFixture(connection, db);
    }

    public static IOptions<PermitSignalOptions> Options(PermitSignalOptions? options = null)
        => Microsoft.Extensions.Options.Options.Create(options ?? new PermitSignalOptions());

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
