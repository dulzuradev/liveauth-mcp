using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LiveAuthCore.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdminLoginSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: false),
                    AmountSats = table.Column<long>(type: "INTEGER", nullable: false),
                    InvoiceBolt11 = table.Column<string>(type: "TEXT", nullable: false),
                    InvoiceRHash = table.Column<string>(type: "TEXT", nullable: false),
                    IsPaid = table.Column<bool>(type: "INTEGER", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PayerLightningAuthKey = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminLoginSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdminPaymentSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AmountSats = table.Column<long>(type: "INTEGER", nullable: false),
                    InvoiceBolt11 = table.Column<string>(type: "TEXT", nullable: false),
                    InvoiceRHash = table.Column<string>(type: "TEXT", nullable: false),
                    IsPaid = table.Column<bool>(type: "INTEGER", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminPaymentSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdminSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Username = table.Column<string>(type: "TEXT", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    PasswordSalt = table.Column<string>(type: "TEXT", nullable: false),
                    IsOwner = table.Column<bool>(type: "INTEGER", nullable: false),
                    Token = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AgentSatsBalances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AgentId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Balance = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalEarned = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalSpent = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentSatsBalances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Developers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LightningAuthKey = table.Column<string>(type: "TEXT", nullable: true),
                    GitHubId = table.Column<string>(type: "TEXT", nullable: true),
                    GitHubUsername = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Developers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DevLoginSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: false),
                    InvoiceId = table.Column<string>(type: "TEXT", nullable: false),
                    InvoiceBolt11 = table.Column<string>(type: "TEXT", nullable: false),
                    AmountSats = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsPaid = table.Column<bool>(type: "INTEGER", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PayerLightningAuthKey = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DevLoginSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EcashProofs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    MintUrl = table.Column<string>(type: "TEXT", nullable: false),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    KeysetId = table.Column<string>(type: "TEXT", nullable: false),
                    Secret = table.Column<string>(type: "TEXT", nullable: false),
                    C = table.Column<string>(type: "TEXT", nullable: false),
                    IsSpent = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SpentAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MintRequestId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EcashProofs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "McpGateSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PowChallengeHex = table.Column<string>(type: "TEXT", nullable: true),
                    PowDifficultyBits = table.Column<int>(type: "INTEGER", nullable: true),
                    PowExpiresAtUnix = table.Column<long>(type: "INTEGER", nullable: true),
                    PowSignature = table.Column<string>(type: "TEXT", nullable: true),
                    LightningInvoice = table.Column<string>(type: "TEXT", nullable: true),
                    LightningPaymentHash = table.Column<string>(type: "TEXT", nullable: true),
                    SatsPerCallAtStart = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpGateSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "McpGateTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    JwtId = table.Column<string>(type: "TEXT", nullable: false),
                    RefreshToken = table.Column<string>(type: "TEXT", nullable: true),
                    IssuedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CallsUsed = table.Column<long>(type: "INTEGER", nullable: false),
                    SatsUsed = table.Column<long>(type: "INTEGER", nullable: false),
                    MaxCallsPerMinute = table.Column<long>(type: "INTEGER", nullable: false),
                    MaxSatsPerDay = table.Column<long>(type: "INTEGER", nullable: false),
                    DayWindowStart = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpGateTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MintProviders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    MintUrl = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MintProviders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MintRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    MintUrl = table.Column<string>(type: "TEXT", nullable: false),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    PaymentHash = table.Column<string>(type: "TEXT", nullable: true),
                    Invoice = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MintRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NostrAgentSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NpubHex = table.Column<string>(type: "TEXT", nullable: false),
                    Lud16 = table.Column<string>(type: "TEXT", nullable: true),
                    Challenge = table.Column<string>(type: "TEXT", nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NostrAgentSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PowUsedNonces",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChallengeHex = table.Column<string>(type: "TEXT", nullable: false),
                    Nonce = table.Column<string>(type: "TEXT", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PowUsedNonces", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RevokedTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Token = table.Column<string>(type: "TEXT", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RevokedTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SatsInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AgentId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    AmountSats = table.Column<long>(type: "INTEGER", nullable: false),
                    PaymentRequest = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    PaymentHash = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SatsInvoices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UsageEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    SatsCharged = table.Column<long>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserEcashBalances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    MintUrl = table.Column<string>(type: "TEXT", nullable: false),
                    Balance = table.Column<long>(type: "INTEGER", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserEcashBalances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeveloperLoginSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeveloperId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DeveloperEmail = table.Column<string>(type: "TEXT", nullable: false),
                    AmountSats = table.Column<long>(type: "INTEGER", nullable: false),
                    PaymentHashB64 = table.Column<string>(type: "TEXT", nullable: false),
                    Invoice = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeveloperLoginSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeveloperLoginSessions_Developers_DeveloperId",
                        column: x => x.DeveloperId,
                        principalTable: "Developers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeveloperId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    PublicKey = table.Column<string>(type: "TEXT", nullable: false),
                    SecretKeyHash = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    MonthlyQuota = table.Column<long>(type: "INTEGER", nullable: false),
                    MonthlyUsed = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    WebhookUrl = table.Column<string>(type: "TEXT", nullable: true),
                    WebhookSecret = table.Column<string>(type: "TEXT", nullable: true),
                    Environment = table.Column<string>(type: "TEXT", nullable: false),
                    AllowedDomainsRaw = table.Column<string>(type: "TEXT", nullable: false),
                    SatsPerLogin = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxAuthsPerIpPerHour = table.Column<int>(type: "INTEGER", nullable: false),
                    AllowDemoAuth = table.Column<bool>(type: "INTEGER", nullable: false),
                    MonthlyAuthCount = table.Column<int>(type: "INTEGER", nullable: false),
                    MonthlyAuthPeriodStart = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Plan = table.Column<string>(type: "TEXT", nullable: false),
                    ProPaidUntil = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UseCustomNode = table.Column<bool>(type: "INTEGER", nullable: false),
                    LndBaseUrl = table.Column<string>(type: "TEXT", nullable: true),
                    LndMacaroon = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "BLOB", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Projects_Developers_DeveloperId",
                        column: x => x.DeveloperId,
                        principalTable: "Developers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgentAuthSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AgentId = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Challenge = table.Column<string>(type: "TEXT", nullable: false),
                    DifficultyBits = table.Column<int>(type: "INTEGER", nullable: false),
                    Solution = table.Column<string>(type: "TEXT", nullable: true),
                    IsVerified = table.Column<bool>(type: "INTEGER", nullable: false),
                    AuthToken = table.Column<string>(type: "TEXT", nullable: true),
                    SolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentAuthSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentAuthSessions_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "auth_event_log",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    occurred_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    event_type = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    request_id = table.Column<string>(type: "TEXT", nullable: false),
                    ip_masked = table.Column<string>(type: "TEXT", nullable: true),
                    sats = table.Column<int>(type: "INTEGER", nullable: true),
                    reason = table.Column<string>(type: "TEXT", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_event_log", x => x.Id);
                    table.ForeignKey(
                        name: "FK_auth_event_log_Projects_project_id",
                        column: x => x.project_id,
                        principalTable: "Projects",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AuthSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Environment = table.Column<string>(type: "TEXT", nullable: true),
                    UserHint = table.Column<string>(type: "TEXT", nullable: true),
                    AmountSats = table.Column<long>(type: "INTEGER", nullable: false),
                    InvoiceRHash = table.Column<string>(type: "TEXT", nullable: true),
                    InvoiceBolt11 = table.Column<string>(type: "TEXT", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsPaid = table.Column<bool>(type: "INTEGER", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PayerLightningAuthKey = table.Column<string>(type: "TEXT", nullable: true),
                    ClientIp = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthSessions_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BillingSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Plan = table.Column<string>(type: "TEXT", nullable: false),
                    AmountSats = table.Column<long>(type: "INTEGER", nullable: false),
                    InvoiceBolt11 = table.Column<string>(type: "TEXT", nullable: false),
                    InvoiceRHash = table.Column<string>(type: "TEXT", nullable: false),
                    IsPaid = table.Column<bool>(type: "INTEGER", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillingSubscriptions_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "McpProxies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    UpstreamUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    SatsPerRequest = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CustomPath = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TotalRequests = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalSatsEarned = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpProxies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_McpProxies_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectApiKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PublicKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SecretKeyHash = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectApiKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectApiKeys_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VerificationSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserRef = table.Column<string>(type: "TEXT", nullable: false),
                    AmountSats = table.Column<long>(type: "INTEGER", nullable: false),
                    PaymentHashB64 = table.Column<string>(type: "TEXT", nullable: false),
                    Invoice = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerificationSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VerificationSessions_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WebhookEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastStatusCode = table.Column<int>(type: "INTEGER", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", nullable: true),
                    DeliveredAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebhookEvents_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuthEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ApiKeyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EventType = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClientIp = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Success = table.Column<bool>(type: "INTEGER", nullable: false),
                    SatsPaid = table.Column<long>(type: "INTEGER", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthEvents_ProjectApiKeys_ApiKeyId",
                        column: x => x.ApiKeyId,
                        principalTable: "ProjectApiKeys",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AuthEvents_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminLoginSessions_Email_CreatedAt",
                table: "AdminLoginSessions",
                columns: new[] { "Email", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminLoginSessions_InvoiceRHash",
                table: "AdminLoginSessions",
                column: "InvoiceRHash");

            migrationBuilder.CreateIndex(
                name: "IX_AdminLoginSessions_IsPaid_ExpiresAt",
                table: "AdminLoginSessions",
                columns: new[] { "IsPaid", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentAuthSessions_ProjectId",
                table: "AgentAuthSessions",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "idx_auth_event_log_project",
                table: "auth_event_log",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "idx_auth_event_log_time",
                table: "auth_event_log",
                column: "occurred_at_utc",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "idx_auth_event_log_type",
                table: "auth_event_log",
                column: "event_type");

            migrationBuilder.CreateIndex(
                name: "IX_AuthEvents_ApiKeyId",
                table: "AuthEvents",
                column: "ApiKeyId");

            migrationBuilder.CreateIndex(
                name: "IX_AuthEvents_ProjectId",
                table: "AuthEvents",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_AuthSessions_ProjectId_ClientIp_CreatedAt",
                table: "AuthSessions",
                columns: new[] { "ProjectId", "ClientIp", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingSubscriptions_InvoiceRHash",
                table: "BillingSubscriptions",
                column: "InvoiceRHash",
                unique: true,
                filter: "\"InvoiceRHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BillingSubscriptions_ProjectId",
                table: "BillingSubscriptions",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingSubscriptions_ProjectId_IsPaid",
                table: "BillingSubscriptions",
                columns: new[] { "ProjectId", "IsPaid" });

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperLoginSessions_DeveloperId",
                table: "DeveloperLoginSessions",
                column: "DeveloperId");

            migrationBuilder.CreateIndex(
                name: "IX_Developers_Email",
                table: "Developers",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Developers_LightningAuthKey",
                table: "Developers",
                column: "LightningAuthKey",
                unique: true,
                filter: "\"LightningAuthKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EcashProofs_MintRequestId",
                table: "EcashProofs",
                column: "MintRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_EcashProofs_Secret",
                table: "EcashProofs",
                column: "Secret",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EcashProofs_UserId_IsSpent",
                table: "EcashProofs",
                columns: new[] { "UserId", "IsSpent" });

            migrationBuilder.CreateIndex(
                name: "IX_EcashProofs_UserId_MintUrl_IsSpent",
                table: "EcashProofs",
                columns: new[] { "UserId", "MintUrl", "IsSpent" });

            migrationBuilder.CreateIndex(
                name: "IX_McpProxies_ProjectId",
                table: "McpProxies",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_MintRequests_PaymentHash",
                table: "MintRequests",
                column: "PaymentHash");

            migrationBuilder.CreateIndex(
                name: "IX_MintRequests_Status",
                table: "MintRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MintRequests_UserId",
                table: "MintRequests",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PowUsedNonces_ExpiresAt",
                table: "PowUsedNonces",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_PowUsedNonces_ProjectId_ChallengeHex_Nonce",
                table: "PowUsedNonces",
                columns: new[] { "ProjectId", "ChallengeHex", "Nonce" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectApiKeys_ProjectId",
                table: "ProjectApiKeys",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_DeveloperId",
                table: "Projects",
                column: "DeveloperId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_PublicKey",
                table: "Projects",
                column: "PublicKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_SecretKeyHash",
                table: "Projects",
                column: "SecretKeyHash");

            migrationBuilder.CreateIndex(
                name: "IX_UserEcashBalances_UserId_MintUrl",
                table: "UserEcashBalances",
                columns: new[] { "UserId", "MintUrl" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VerificationSessions_PaymentHashB64",
                table: "VerificationSessions",
                column: "PaymentHashB64");

            migrationBuilder.CreateIndex(
                name: "IX_VerificationSessions_ProjectId",
                table: "VerificationSessions",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookEvents_ProjectId",
                table: "WebhookEvents",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminLoginSessions");

            migrationBuilder.DropTable(
                name: "AdminPaymentSessions");

            migrationBuilder.DropTable(
                name: "AdminSessions");

            migrationBuilder.DropTable(
                name: "AgentAuthSessions");

            migrationBuilder.DropTable(
                name: "AgentSatsBalances");

            migrationBuilder.DropTable(
                name: "auth_event_log");

            migrationBuilder.DropTable(
                name: "AuthEvents");

            migrationBuilder.DropTable(
                name: "AuthSessions");

            migrationBuilder.DropTable(
                name: "BillingSubscriptions");

            migrationBuilder.DropTable(
                name: "DeveloperLoginSessions");

            migrationBuilder.DropTable(
                name: "DevLoginSessions");

            migrationBuilder.DropTable(
                name: "EcashProofs");

            migrationBuilder.DropTable(
                name: "McpGateSessions");

            migrationBuilder.DropTable(
                name: "McpGateTokens");

            migrationBuilder.DropTable(
                name: "McpProxies");

            migrationBuilder.DropTable(
                name: "MintProviders");

            migrationBuilder.DropTable(
                name: "MintRequests");

            migrationBuilder.DropTable(
                name: "NostrAgentSessions");

            migrationBuilder.DropTable(
                name: "PowUsedNonces");

            migrationBuilder.DropTable(
                name: "RevokedTokens");

            migrationBuilder.DropTable(
                name: "SatsInvoices");

            migrationBuilder.DropTable(
                name: "UsageEvents");

            migrationBuilder.DropTable(
                name: "UserEcashBalances");

            migrationBuilder.DropTable(
                name: "VerificationSessions");

            migrationBuilder.DropTable(
                name: "WebhookEvents");

            migrationBuilder.DropTable(
                name: "ProjectApiKeys");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropTable(
                name: "Developers");
        }
    }
}
