using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LiveAuthCore.Data.Entities;

public class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DeveloperId { get; set; }
    public Developer Developer { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public string PublicKey { get; set; } = string.Empty;
    public string SecretKeyHash { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public long MonthlyQuota { get; set; } = 500;
    public long MonthlyUsed { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ProjectApiKey> ApiKeys { get; set; } = new List<ProjectApiKey>();

    public string? WebhookUrl { get; set; }
    public string? WebhookSecret { get; set; }

    public string Environment { get; set; } = "TEST";

    public List<string> AllowedDomains { get; set; } = new();

    public int SatsPerLogin { get; set; } = 21;
    public int MaxAuthsPerIpPerHour { get; set; } = 100;

    public bool AllowDemoAuth { get; set; } = false;

    public int MonthlyAuthCount { get; set; }
    public DateTime MonthlyAuthPeriodStart { get; set; }

    public string Plan { get; set; } = "free";
    public DateTime? ProPaidUntil { get; set; }

    // Custom LND node configuration
    public bool UseCustomNode { get; set; } = false;
    public string? LndBaseUrl { get; set; }
    public string? LndMacaroon { get; set; } // Encrypted

    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }

    // L402 balance for MCP per-call metering
    public long L402BalanceSats { get; set; } = 0;

    // MCP LiveAuth Gate configuration
    public int McpSatsPerCall { get; set; } = 1;
    public int McpInvoiceCallCredits { get; set; } = 10;
    public long McpMaxSatsPerDay { get; set; } = 10_000;
    public int McpMaxCallsPerMinute { get; set; } = 60;

    // ✅ FIX: nullable disables EF concurrency + RETURNING
    // Removed TypeName to allow EF Core to map correctly for each DB provider (bytea for Postgres, BLOB for SQLite)
    public byte[]? RowVersion { get; set; } = Guid.NewGuid().ToByteArray();
}

public static class ProjectExtensions
{
    public static bool IsPro(this Project project)
    {
        return project.Plan == "pro" &&
               project.ProPaidUntil.HasValue &&
               project.ProPaidUntil.Value > DateTime.UtcNow;
    }
}

public static class SubscriptionPricing
{
    public const long ProMonthlySats = 100_00;
    public static readonly TimeSpan ProDuration = TimeSpan.FromDays(30);
}
