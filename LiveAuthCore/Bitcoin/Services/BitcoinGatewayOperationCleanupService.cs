using LiveAuthCore.Bitcoin.Configuration;
using LiveAuthCore.Data;
using LiveAuthCore.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LiveAuthCore.Bitcoin.Services;

public sealed class BitcoinGatewayOperationCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IOptionsMonitor<BitcoinGatewayOptions> _options;
    private readonly ILogger<BitcoinGatewayOperationCleanupService> _logger;

    public BitcoinGatewayOperationCleanupService(
        IServiceScopeFactory scopes,
        IOptionsMonitor<BitcoinGatewayOptions> options,
        ILogger<BitcoinGatewayOperationCleanupService> logger)
    {
        _scopes = scopes;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bitcoin Gateway operation cleanup failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(Math.Clamp(
                    _options.CurrentValue.CleanupIntervalHours, 1, 168)), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    internal async Task<int> CleanupAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-Math.Clamp(
            _options.CurrentValue.OperationRetentionDays, 1, 3650));
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        var meter = scope.ServiceProvider.GetRequiredService<IMcpToolMeteringService>();
        var expired = await db.BitcoinGatewayOperations
            .Where(item => item.UpdatedAt < cutoff)
            .OrderBy(item => item.UpdatedAt)
            .Take(500)
            .ToListAsync(ct);
        foreach (var operation in expired)
        {
            if (operation.RevenueEventId.HasValue && operation.Status != "Succeeded")
                await meter.CancelReservationAsync(operation.RevenueEventId.Value,
                    "bitcoin_operation_retention_expired", ct);
        }
        db.BitcoinGatewayOperations.RemoveRange(expired);
        await db.SaveChangesAsync(ct);
        return expired.Count;
    }
}
