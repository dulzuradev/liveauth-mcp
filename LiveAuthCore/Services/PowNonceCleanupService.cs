using LiveAuthCore.Data;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Services;

/// <summary>
/// Background service that periodically cleans up expired PoW nonces from the database.
/// Runs every hour to prevent unbounded growth.
/// </summary>
public sealed class PowNonceCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PowNonceCleanupService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromHours(1);

    public PowNonceCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<PowNonceCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PowNonceCleanupService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, stoppingToken);
                await CleanupExpiredNoncesAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during PoW nonce cleanup");
            }
        }

        _logger.LogInformation("PowNonceCleanupService stopped");
    }

    internal async Task<int> CleanupExpiredNoncesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();

        var cutoff = DateTime.UtcNow;
        var deleted = await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM PowUsedNonces WHERE ExpiresAt < {0}",
            cutoff
        );

        if (deleted > 0)
        {
            _logger.LogInformation("Cleaned up {Count} expired PoW nonces", deleted);
        }

        return deleted;
    }
}
