using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace LiveAuthCore.Data.Entities;

public class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DeveloperId { get; set; }
    public Developer Developer { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public string PublicKey { get; set; } = string.Empty;   // unique index
    public string SecretKeyHash { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public long MonthlyQuota { get; set; } = 500;
    public long MonthlyUsed { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ProjectApiKey> ApiKeys { get; set; } = new List<ProjectApiKey>();

    public string? WebhookUrl { get; set; }
    public string? WebhookSecret { get; set; }

    public string Environment { get; set; } = "TEST"; // TEST or LIVE

    // Persisted via ValueConverter
    public List<string> AllowedDomains { get; set; } = new();

    public int SatsPerLogin { get; set; } = 21;
    public int MaxAuthsPerIpPerHour { get; set; } = 100;

    public bool AllowDemoAuth { get; set; } = false;

    public int MonthlyAuthCount { get; set; }
    public DateTime MonthlyAuthPeriodStart { get; set; }

    public string Plan { get; set; } = "free"; 
    // free | pro (string keeps it flexible for v2)

    public DateTime? ProPaidUntil { get; set; } 
    // UTC timestamp when Pro expires

    // 🔑 CRITICAL FIX — EF MUST NOT TREAT THIS AS DB-GENERATED
    [Required]
    [Column(TypeName = "BLOB")]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public byte[] RowVersion { get; set; } = Guid.NewGuid().ToByteArray();
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
    public const long ProMonthlySats = 100_000; // example: 100k sats
    public static readonly TimeSpan ProDuration = TimeSpan.FromDays(30);
}
