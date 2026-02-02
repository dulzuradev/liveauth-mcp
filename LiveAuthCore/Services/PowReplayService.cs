using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Services;

/// <summary>
/// Atomic replay protection using database unique constraint.
/// Prevents race conditions that existed in the cache-based implementation.
/// </summary>
public sealed class PowReplayService
{
    private readonly IDbContextFactory<LiveAuthDbContext> _dbFactory;
    private readonly ILogger<PowReplayService> _logger;

    public PowReplayService(
        IDbContextFactory<LiveAuthDbContext> dbFactory,
        ILogger<PowReplayService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>
    /// Returns true if this nonce has NOT been used before and is now marked as used.
    /// Returns false if it was already used (replay detected).
    /// 
    /// Uses database unique constraint for atomic check-and-insert.
    /// </summary>
    public async Task<bool> TryMarkNonceUsedAsync(
        Guid projectId,
        string challengeHex,
        string clientNonce,
        long expiresAtUnix,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(challengeHex) ||
            string.IsNullOrWhiteSpace(clientNonce))
            return false;

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expiresAtUnix).UtcDateTime;

            var record = new PowUsedNonce
            {
                ProjectId = projectId,
                ChallengeHex = challengeHex,
                Nonce = clientNonce,
                UsedAt = DateTime.UtcNow,
                ExpiresAt = expiresAt
            };

            db.PowUsedNonces.Add(record);
            await db.SaveChangesAsync(ct);

            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Unique constraint violation = nonce was already used (replay)
            _logger.LogWarning("Replay detected: project={ProjectId}, challenge={Challenge}, nonce={Nonce}",
                projectId, challengeHex, clientNonce);
            return false;
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        // SQLite unique constraint violation message
        return ex.InnerException?.Message?.Contains("UNIQUE constraint failed") == true;
    }
}