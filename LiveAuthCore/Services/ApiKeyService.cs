using System.Security.Cryptography;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Services;

public class ApiKeyService
{
    private readonly LiveAuthDbContext _db;
    private readonly PasswordHasher<Project> _hasher = new();
    private readonly AuthEventService _authEvents;

    public ApiKeyService(LiveAuthDbContext db, AuthEventService authEvents)
    {
        _db = db;
        _authEvents = authEvents;
    }

    // ---------------------------------------------------------------------
    // KEY GENERATION
    // ---------------------------------------------------------------------

    public (string PublicKey, string SecretKey, string SecretKeyHash) GenerateKeys()
    {
        var pub = "la_pk_" + Base64UrlEncode(RandomNumberGenerator.GetBytes(18));
        var sec = "la_sk_" + Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

        var dummyProject = new Project();
        var secHash = _hasher.HashPassword(dummyProject, sec);

        return (pub, sec, secHash);
    }

    public (string SecretKey, string SecretKeyHash) GenerateNewSecret()
    {
        var sec = "la_sk_" + Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var dummyProject = new Project();
        var secHash = _hasher.HashPassword(dummyProject, sec);
        return (sec, secHash);
    }

    // ---------------------------------------------------------------------
    // CREATE PROJECT API KEY
    // ---------------------------------------------------------------------

    public async Task<(ProjectApiKey apiKey, string secret)> CreateApiKeyForProjectAsync(
        Project project,
        string label,
        CancellationToken ct = default)
    {
        var trimmedLabel = string.IsNullOrWhiteSpace(label)
            ? "Default key"
            : label.Trim();

        var (publicKey, secretKey, secretHash) = GenerateKeys();

        var apiKey = new ProjectApiKey
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Label = trimmedLabel,
            PublicKey = publicKey,
            SecretKeyHash = secretHash,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        _db.ProjectApiKeys.Add(apiKey);
        await _db.SaveChangesAsync(ct);

        return (apiKey, secretKey);
    }

    // ---------------------------------------------------------------------
    // SECRET KEY AUTH (SERVER-SIDE)
    // ---------------------------------------------------------------------

    public async Task<ApiKeyAuthResult> AuthenticateApiKeyAsync(
        string secretKey,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(secretKey))
            return ApiKeyAuthResult.Invalid();

        // v2 API keys
        var apiKeys = await _db.ProjectApiKeys
            .Include(k => k.Project)
            .Where(k => k.Project.IsActive)
            .ToListAsync(ct);

        foreach (var key in apiKeys)
        {
            var result = _hasher.VerifyHashedPassword(
                key.Project,
                key.SecretKeyHash,
                secretKey);

            if (result == PasswordVerificationResult.Success)
            {
                if (!key.IsActive)
                    return ApiKeyAuthResult.Revoked();

                key.LastUsedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);

                return ApiKeyAuthResult.Ok(key.Project, key);
            }
        }

        // v1 legacy fallback
        var projects = await _db.Projects
            .Where(p => p.IsActive)
            .ToListAsync(ct);

        foreach (var project in projects)
        {
            var result = _hasher.VerifyHashedPassword(
                project,
                project.SecretKeyHash,
                secretKey);

            if (result == PasswordVerificationResult.Success)
                return ApiKeyAuthResult.Ok(project, null);
        }

        return ApiKeyAuthResult.Invalid();
    }

    public async Task<Project?> AuthenticateProjectAsync(
        string secretKey,
        CancellationToken ct = default)
    {
        var result = await AuthenticateApiKeyAsync(secretKey, ct);
        return result.Status == ApiKeyAuthStatus.Ok ? result.Project : null;
    }

    // ---------------------------------------------------------------------
    // PUBLIC KEY AUTH (PoW / Browser)
    // ---------------------------------------------------------------------
    // ✔ Accepts PROJECT public keys (la_pk_...)
    // ✔ Does NOT require ProjectApiKeys
    // ✔ Enforces LIVE + active
    // ---------------------------------------------------------------------

    public async Task<ApiKeyAuthResult> AuthenticatePublicKeyAsync(
        string publicKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(publicKey))
            return ApiKeyAuthResult.Invalid();

        publicKey = publicKey.Trim();

        if (!publicKey.StartsWith("la_pk_", StringComparison.Ordinal))
            return ApiKeyAuthResult.Invalid();

        // ✅ PRIMARY PATH — PROJECT public key (PoW)
        var project = await _db.Projects
            .SingleOrDefaultAsync(p =>
                    p.PublicKey == publicKey &&
                    p.IsActive &&
                    (
                        p.Environment == "LIVE" ||
                        p.AllowDemoAuth
                    ),
                ct);

        if (project != null)
            return ApiKeyAuthResult.Ok(project, apiKey: null);

        // 🔁 SECONDARY PATH — API key public key (future-proof)
        var apiKey = await _db.ProjectApiKeys
            .Include(k => k.Project)
            .SingleOrDefaultAsync(k =>
                k.PublicKey == publicKey &&
                k.IsActive &&
                k.Project.IsActive,
                ct);

        if (apiKey == null)
            return ApiKeyAuthResult.Invalid();

        apiKey.LastUsedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return ApiKeyAuthResult.Ok(apiKey.Project, apiKey);
    }

    // ---------------------------------------------------------------------

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
}

// ========================================================================
// RESULT TYPES
// ========================================================================

public enum ApiKeyAuthStatus
{
    Ok,
    Invalid,
    Revoked
}

public sealed class ApiKeyAuthResult
{
    public ApiKeyAuthStatus Status { get; }
    public Project? Project { get; }
    public ProjectApiKey? ApiKey { get; }

    private ApiKeyAuthResult(
        ApiKeyAuthStatus status,
        Project? project,
        ProjectApiKey? apiKey)
    {
        Status = status;
        Project = project;
        ApiKey = apiKey;
    }

    public static ApiKeyAuthResult Ok(Project project, ProjectApiKey? apiKey)
        => new(ApiKeyAuthStatus.Ok, project, apiKey);

    public static ApiKeyAuthResult Invalid()
        => new(ApiKeyAuthStatus.Invalid, null, null);

    public static ApiKeyAuthResult Revoked()
        => new(ApiKeyAuthStatus.Revoked, null, null);
}
