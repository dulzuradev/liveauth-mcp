using FluentAssertions;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LiveAuthCore.Tests.Services;

public class CleanupWorkerTests
{
    [Fact]
    public async Task DevLoginCleanup_RemovesOnlyUnpaidSessionsExpiredBeyondMaxAge()
    {
        await using var services = CreateInMemoryServices();
        var oldUnpaidId = Guid.NewGuid();
        var oldPaidId = Guid.NewGuid();
        var recentUnpaidId = Guid.NewGuid();
        await SeedDevLoginSessionsAsync(
            services,
            CreateDevLoginSession(oldUnpaidId, DateTime.UtcNow.AddHours(-2), isPaid: false),
            CreateDevLoginSession(oldPaidId, DateTime.UtcNow.AddHours(-2), isPaid: true),
            CreateDevLoginSession(recentUnpaidId, DateTime.UtcNow.AddMinutes(-30), isPaid: false));
        var cleanup = new DevLoginSessionCleanupService(
            services,
            NullLogger<DevLoginSessionCleanupService>.Instance);

        var deleted = await cleanup.CleanupExpiredSessionsAsync(CancellationToken.None);

        deleted.Should().Be(1);
        var remainingIds = await GetDevLoginSessionIdsAsync(services);
        remainingIds.Should().BeEquivalentTo(new[] { oldPaidId, recentUnpaidId });
    }

    [Fact]
    public async Task PowNonceCleanup_RemovesExpiredNoncesAndKeepsActiveOnes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var services = CreateSqliteServices(connection);
        await EnsureDatabaseCreatedAsync(services);
        var expiredId = await SeedPowNonceAsync(services, DateTime.UtcNow.AddMinutes(-1));
        var activeId = await SeedPowNonceAsync(services, DateTime.UtcNow.AddMinutes(10));
        var cleanup = new PowNonceCleanupService(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PowNonceCleanupService>.Instance);

        var deleted = await cleanup.CleanupExpiredNoncesAsync(CancellationToken.None);

        deleted.Should().Be(1);
        var remainingIds = await GetPowNonceIdsAsync(services);
        remainingIds.Should().BeEquivalentTo(new[] { activeId });
        remainingIds.Should().NotContain(expiredId);
    }

    private static ServiceProvider CreateInMemoryServices()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var databaseName = $"CleanupWorkerTests_{Guid.NewGuid():N}";

        return new ServiceCollection()
            .AddDbContext<LiveAuthDbContext>(options =>
                options.UseInMemoryDatabase(databaseName, databaseRoot))
            .BuildServiceProvider();
    }

    private static ServiceProvider CreateSqliteServices(SqliteConnection connection)
    {
        return new ServiceCollection()
            .AddDbContext<LiveAuthDbContext>(options => options.UseSqlite(connection))
            .BuildServiceProvider();
    }

    private static async Task EnsureDatabaseCreatedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    private static async Task SeedDevLoginSessionsAsync(
        IServiceProvider services,
        params DevLoginSession[] sessions)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        db.DevLoginSessions.AddRange(sessions);
        await db.SaveChangesAsync();
    }

    private static async Task<IReadOnlyList<Guid>> GetDevLoginSessionIdsAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        return await db.DevLoginSessions
            .Select(session => session.Id)
            .ToListAsync();
    }

    private static async Task<long> SeedPowNonceAsync(
        IServiceProvider services,
        DateTime expiresAt)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        var nonce = new PowUsedNonce
        {
            ProjectId = Guid.NewGuid(),
            ChallengeHex = Guid.NewGuid().ToString("N"),
            Nonce = Guid.NewGuid().ToString("N"),
            UsedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt
        };
        db.PowUsedNonces.Add(nonce);
        await db.SaveChangesAsync();
        return nonce.Id;
    }

    private static async Task<IReadOnlyList<long>> GetPowNonceIdsAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        return await db.PowUsedNonces
            .Select(nonce => nonce.Id)
            .ToListAsync();
    }

    private static DevLoginSession CreateDevLoginSession(
        Guid id,
        DateTime expiresAt,
        bool isPaid)
    {
        return new DevLoginSession
        {
            Id = id,
            Email = $"{id:N}@liveauth.test",
            InvoiceId = $"invoice-{id:N}",
            InvoiceBolt11 = $"lnbc{id:N}",
            AmountSats = 100,
            BaseAmountSats = 100,
            TotalChargedSats = 100,
            CreditAmountSats = 100,
            ExpiresAt = expiresAt,
            IsPaid = isPaid,
            PaidAt = isPaid ? DateTime.UtcNow.AddMinutes(-10) : null
        };
    }
}
