using LiveAuthCore.Data;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Services;

public class DevLoginSessionCleanupService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<DevLoginSessionCleanupService> _logger;

    // How often to run the cleanup
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

    // How old an expired session can be before we delete it
    private readonly TimeSpan _maxAge = TimeSpan.FromHours(1);

    public DevLoginSessionCleanupService(
        IServiceProvider services,
        ILogger<DevLoginSessionCleanupService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DevLoginSessionCleanupService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, stoppingToken);

                using var scope = _services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();

                var cutoff = DateTime.UtcNow - _maxAge;

                var expired = await db.DevLoginSessions
                    .Where(s => s.ExpiresAt < cutoff && !s.IsPaid)
                    .ToListAsync(stoppingToken);

                if (expired.Count > 0)
                {
                    _logger.LogInformation(
                        "Cleaning up {Count} expired dev login sessions older than {Cutoff}.",
                        expired.Count, cutoff);

                    db.DevLoginSessions.RemoveRange(expired);
                    await db.SaveChangesAsync(stoppingToken);
                }
            }
            catch (TaskCanceledException)
            {
                // shutting down
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during dev login session cleanup.");
            }
        }

        _logger.LogInformation("DevLoginSessionCleanupService stopped.");
    }
}
