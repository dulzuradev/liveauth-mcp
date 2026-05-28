using System.Text.Json;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Data.Entities.Mcp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace LiveAuthCore.Data;

public class LiveAuthDbContext : DbContext
{
    public LiveAuthDbContext(DbContextOptions<LiveAuthDbContext> options) : base(options) { }

    public DbSet<Developer> Developers => Set<Developer>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<VerificationSession> VerificationSessions => Set<VerificationSession>();
    public DbSet<UsageEvent> UsageEvents => Set<UsageEvent>();
    public DbSet<DeveloperLoginSession> DeveloperLoginSessions => Set<DeveloperLoginSession>();
    public DbSet<DevLoginSession> DevLoginSessions { get; set; } = default!;

    public DbSet<ProjectApiKey> ProjectApiKeys { get; set; } = null!;
    
    public DbSet<WebhookEvent> WebhookEvents { get; set; } = null!;
    
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<AuthEvent> AuthEvents => Set<AuthEvent>();
    public DbSet<McpProxy> McpProxies => Set<McpProxy>();
    
    public DbSet<BillingSubscription> BillingSubscriptions => Set<BillingSubscription>();
    public DbSet<AdminLoginSession> AdminLoginSessions => Set<AdminLoginSession>();
    public DbSet<AdminSession> AdminSessions => Set<AdminSession>();
    public DbSet<AdminPaymentSession> AdminPaymentSessions => Set<AdminPaymentSession>();
    public DbSet<AgentAuthSession> AgentAuthSessions => Set<AgentAuthSession>();
    public DbSet<AuthEventLog> AuthEventLogs => Set<AuthEventLog>();
    public DbSet<PowUsedNonce> PowUsedNonces => Set<PowUsedNonce>();

    public DbSet<MintRequest> MintRequests => Set<MintRequest>();
    public DbSet<UserEcashBalance> UserEcashBalances => Set<UserEcashBalance>();
    public DbSet<MintProvider> MintProviders => Set<MintProvider>();
    public DbSet<EcashProof> EcashProofs => Set<EcashProof>();

    // Agent Sats (LND-based)
    public DbSet<AgentSatsBalance> AgentSatsBalances => Set<AgentSatsBalance>();
    public DbSet<SatsInvoice> SatsInvoices => Set<SatsInvoice>();

    // MCP LiveAuth Gate
    public DbSet<McpGateSession> McpGateSessions => Set<McpGateSession>();
    public DbSet<McpGateToken> McpGateTokens => Set<McpGateToken>();
    public DbSet<McpTool> McpTools => Set<McpTool>();
    public DbSet<McpToolRevenueEvent> McpToolRevenueEvents => Set<McpToolRevenueEvent>();
    public DbSet<L402Purchase> L402Purchases => Set<L402Purchase>();
    public DbSet<L402Bundle> L402Bundles => Set<L402Bundle>();
    public DbSet<L402Macaroon> L402Macaroons => Set<L402Macaroon>();
    public DbSet<WaitlistLead> WaitlistLeads => Set<WaitlistLead>();

    // Nostr Agent Auth
    public DbSet<NostrAgentSession> NostrAgentSessions => Set<NostrAgentSession>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // In this project we sometimes bootstrap schema via EnsureCreated + raw SQL guards.
        // This can cause EF to believe there are pending model changes when applying targeted migrations.
        // Suppress the PendingModelChangesWarning so that `dotnet ef database update` can run specific migrations
        // without requiring a full model snapshot alignment.
        // optionsBuilder.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>()
            .HasIndex(p => p.PublicKey)
            .IsUnique();

        modelBuilder.Entity<Project>()
            .HasIndex(p => p.SecretKeyHash);

        modelBuilder.Entity<VerificationSession>()
            .HasIndex(s => s.PaymentHashB64);
        
        modelBuilder.Entity<Developer>()
            .HasIndex(d => d.LightningAuthKey)
            .IsUnique()
            .HasFilter("\"LightningAuthKey\" IS NOT NULL");

        modelBuilder.Entity<Developer>()
            .HasIndex(d => d.Email)
            .IsUnique();

        var stringListConverter = new ValueConverter<List<string>, string>(
            v => JsonSerializer.Serialize(
                v ?? new List<string>(),
                (JsonSerializerOptions?)null
            ),
            v => string.IsNullOrWhiteSpace(v)
                ? new List<string>()
                : JsonSerializer.Deserialize<List<string>>(
                    v,
                    (JsonSerializerOptions?)null
                ) ?? new List<string>());

        modelBuilder.Entity<Project>()
            .Property(p => p.AllowedDomains)
            .HasColumnName("AllowedDomainsRaw")
            .HasConversion(stringListConverter)
            .HasColumnType("TEXT");

        modelBuilder.Entity<AuthSession>()
            .HasIndex(s => new { s.ProjectId, s.ClientIp, s.CreatedAt });
        
        // AuthEvent indexes for efficient querying by project and event type
        modelBuilder.Entity<AuthEvent>()
            .HasIndex(e => e.ProjectId);
        
        modelBuilder.Entity<AuthEvent>()
            .HasIndex(e => e.EventType);
        
        modelBuilder.Entity<AuthEvent>()
            .HasIndex(e => new { e.ProjectId, e.EventType });
        
        modelBuilder.Entity<BillingSubscription>()
            .HasIndex(x => x.InvoiceRHash)
            .IsUnique()
            .HasFilter("\"InvoiceRHash\" IS NOT NULL");
        
        modelBuilder.Entity<BillingSubscription>()
            .HasIndex(x => x.ProjectId);

        modelBuilder.Entity<BillingSubscription>()
            .HasIndex(x => new { x.ProjectId, x.IsPaid });

        modelBuilder.Entity<Project>()
            .Property(p => p.RowVersion)
            .IsRowVersion();

        modelBuilder.Entity<Project>()
            .Property(p => p.LndBaseUrl)
            .HasColumnName("LndBaseUrl");

        modelBuilder.Entity<Project>()
            .Property(p => p.LndMacaroon)
            .HasColumnName("LndMacaroon");

        modelBuilder.Entity<Project>()
            .Property(p => p.UseCustomNode)
            .HasColumnName("UseCustomNode");
        
        // Unique constraint for replay protection (atomic check-and-insert)
        modelBuilder.Entity<PowUsedNonce>()
            .HasIndex(n => new { n.ProjectId, n.ChallengeHex, n.Nonce })
            .IsUnique();
        
        // Index for cleanup of expired nonces
        modelBuilder.Entity<PowUsedNonce>()
            .HasIndex(n => n.ExpiresAt);
        
        // modelBuilder.Entity<BillingSubscription>()
        //     .Property(x => x.RowVersion)
        //     .IsRowVersion()
        //     .IsConcurrencyToken()
        //     .ValueGeneratedOnAddOrUpdate()
        //     .IsRequired(false);
        
        modelBuilder.Entity<AdminLoginSession>()
            .HasIndex(x => x.InvoiceRHash);

        modelBuilder.Entity<AdminLoginSession>()
            .HasIndex(x => new { x.Email, x.CreatedAt });

        modelBuilder.Entity<AdminLoginSession>()
            .HasIndex(x => new { x.IsPaid, x.ExpiresAt });

        modelBuilder.Entity<AuthEventLog>(entity =>
        {
            entity.ToTable("auth_event_log");
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.OccurredAtUtc).HasColumnName("occurred_at_utc");
            entity.Property(e => e.EventType).HasColumnName("event_type");
            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.RequestId).HasColumnName("request_id");
            entity.Property(e => e.IpMasked).HasColumnName("ip_masked");
            entity.Property(e => e.Sats).HasColumnName("sats");
            entity.Property(e => e.Reason).HasColumnName("reason");
            entity.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb");

            entity.HasIndex(e => e.OccurredAtUtc).HasDatabaseName("idx_auth_event_log_time").IsDescending();
            entity.HasIndex(e => e.ProjectId).HasDatabaseName("idx_auth_event_log_project");
            entity.HasIndex(e => e.EventType).HasDatabaseName("idx_auth_event_log_type");
        });

        modelBuilder.Entity<MintRequest>()
            .HasIndex(m => m.UserId);

        modelBuilder.Entity<MintRequest>()
            .HasIndex(m => m.PaymentHash);

        modelBuilder.Entity<MintRequest>()
            .HasIndex(m => m.Status);

        modelBuilder.Entity<UserEcashBalance>()
            .HasIndex(b => new { b.UserId, b.MintUrl })
            .IsUnique();

        modelBuilder.Entity<EcashProof>()
            .HasIndex(p => new { p.UserId, p.IsSpent });

        modelBuilder.Entity<EcashProof>()
            .HasIndex(p => new { p.UserId, p.MintUrl, p.IsSpent });

        modelBuilder.Entity<EcashProof>()
            .HasIndex(p => p.MintRequestId);

        modelBuilder.Entity<EcashProof>()
            .HasIndex(p => p.Secret)
            .IsUnique();

        modelBuilder.Entity<L402Bundle>()
            .HasIndex(b => b.BundleId)
            .IsUnique();

        modelBuilder.Entity<L402Bundle>()
            .HasIndex(b => b.PaymentHash);

        modelBuilder.Entity<L402Bundle>()
            .HasIndex(b => b.ProjectId);

        modelBuilder.Entity<L402Macaroon>()
            .HasIndex(m => m.Jti)
            .IsUnique();

        modelBuilder.Entity<L402Macaroon>()
            .HasIndex(m => new { m.BundleId, m.IsRevoked });
        
        modelBuilder.Entity<L402Macaroon>()
            .HasIndex(m => m.BundleId);

        modelBuilder.Entity<McpTool>()
            .HasIndex(t => t.Slug)
            .IsUnique();

        modelBuilder.Entity<McpToolRevenueEvent>()
            .HasIndex(e => new { e.McpToolId, e.CreatedAt });

        modelBuilder.Entity<McpToolRevenueEvent>()
            .HasIndex(e => new { e.PayingProjectId, e.CreatedAt });

        modelBuilder.Entity<McpToolRevenueEvent>()
            .HasIndex(e => e.McpGateTokenId);

        modelBuilder.Entity<McpToolRevenueEvent>()
            .HasIndex(e => new { e.McpToolId, e.IdempotencyKey })
            .IsUnique()
            .HasFilter("\"IdempotencyKey\" IS NOT NULL");

        modelBuilder.Entity<WaitlistLead>()
            .HasIndex(l => l.Email)
            .IsUnique();
    }
}
