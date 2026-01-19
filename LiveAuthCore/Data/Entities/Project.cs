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

    // ✅ FIX: nullable disables EF concurrency + RETURNING
    [Column(TypeName = "BLOB")]
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
    public const long ProMonthlySats = 100_000;
    public static readonly TimeSpan ProDuration = TimeSpan.FromDays(30);
}