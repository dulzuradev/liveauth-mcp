using LiveAuthCore.Bitcoin.Configuration;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities.Mcp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LiveAuthCore.Bitcoin.Services;

public interface IBitcoinGatewayBootstrapper
{
    Task SeedAsync(CancellationToken ct = default);
}

public sealed class BitcoinGatewayBootstrapper : IBitcoinGatewayBootstrapper
{
    private readonly LiveAuthDbContext _db;
    private readonly BitcoinGatewayOptions _options;
    private readonly IConfiguration _configuration;

    public BitcoinGatewayBootstrapper(
        LiveAuthDbContext db,
        IOptions<BitcoinGatewayOptions> options,
        IConfiguration configuration)
    {
        _db = db;
        _options = options.Value;
        _configuration = configuration;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        Guid? projectId = null;
        var configuredProject = _configuration["BitcoinGateway:ProjectId"] ??
                                _configuration["LiveAuth:DemoProjectId"];
        if (Guid.TryParse(configuredProject, out var parsed)) projectId = parsed;

        var tools = new[]
        {
            Tool("00000000-0000-0000-0000-000000000010", "Bitcoin Fee Estimates",
                BitcoinGatewayTools.FeeEstimates,
                "Query node-backed Bitcoin fee estimates for 1, 3, 6, 25, and 144-block targets. Estimates are observations, not confirmation guarantees.",
                _options.Tools.FeeEstimates.PriceSats, projectId),
            Tool("00000000-0000-0000-0000-000000000011", "Bitcoin Mempool Summary",
                BitcoinGatewayTools.MempoolSummary,
                "Query a compact Bitcoin node mempool summary without downloading the full mempool.",
                _options.Tools.MempoolSummary.PriceSats, projectId),
            Tool("00000000-0000-0000-0000-000000000012", "Bitcoin Transaction Preflight",
                BitcoinGatewayTools.PreflightTransaction,
                "Evaluate caller-supplied raw transaction hex with Bitcoin Core testmempoolaccept. This operation NEVER broadcasts the transaction.",
                _options.Tools.PreflightTransaction.PriceSats, projectId),
            Tool("00000000-0000-0000-0000-000000000013", "Bitcoin Transaction Broadcast",
                BitcoinGatewayTools.BroadcastTransaction,
                "Preflight and, only if accepted by policy, submit caller-supplied raw transaction hex to the Bitcoin network. This operation CAN broadcast a transaction.",
                _options.Tools.BroadcastTransaction.PriceSats, projectId),
            Tool("00000000-0000-0000-0000-000000000014", "Bitcoin Transaction Status",
                BitcoinGatewayTools.TransactionStatus,
                "Observe whether a Bitcoin transaction is in the node mempool, confirmed, or not found.",
                _options.Tools.TransactionStatus.PriceSats, projectId),
            AnonymousTool()
        };

        foreach (var configured in tools)
        {
            var existing = await _db.McpTools.SingleOrDefaultAsync(item => item.Slug == configured.Slug, ct);
            if (existing == null)
            {
                _db.McpTools.Add(configured);
                continue;
            }
            existing.ProjectId = configured.ProjectId;
            existing.Name = configured.Name;
            existing.Description = configured.Description;
            existing.Category = configured.Category;
            existing.DefaultCostSats = configured.DefaultCostSats;
            existing.MinCostSats = configured.MinCostSats;
            existing.MaxCostSats = configured.MaxCostSats;
            existing.Status = "Active";
            existing.Visibility = configured.Visibility;
            existing.RemovedAt = null;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// System fallback tool used by <c>McpGateController.Charge</c> when a charge
    /// arrives without a <c>toolName</c>. Routes unattributed charges through the
    /// revenue event pipeline so they still pay the platform fee.
    /// Fixed Guid (<c>00000000-0000-0000-0000-000000000001</c>) so the controller
    /// can look it up by slug; <c>Visibility=Internal</c> keeps it out of public
    /// catalog listings; <c>MaxCostSats=0</c> disables the upper bound so callers
    /// can charge any positive amount up to their daily budget.
    /// </summary>
    private static McpTool AnonymousTool()
        => new()
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            ProjectId = null,
            Name = "Anonymous Agent Call",
            Slug = "anonymous-agent-call",
            Description = "System fallback for MCP charges that omit toolName. Captures revenue events for unattributed calls so they don't bypass the platform fee pipeline.",
            Category = "system",
            Status = "Active",
            Visibility = "Internal",
            DefaultCostSats = 1,
            MinCostSats = 1,
            MaxCostSats = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

    private static McpTool Tool(
        string id,
        string name,
        string slug,
        string description,
        int price,
        Guid? projectId)
        => new()
        {
            Id = Guid.Parse(id),
            ProjectId = projectId,
            Name = name,
            Slug = slug,
            Description = description,
            Category = "bitcoin-infrastructure",
            Status = "Active",
            Visibility = "Unlisted",
            DefaultCostSats = Math.Clamp(price, 1, 1_000_000),
            MinCostSats = Math.Clamp(price, 1, 1_000_000),
            MaxCostSats = Math.Clamp(price, 1, 1_000_000),
            DocsUrl = "https://github.com/dulzuradev/LiveAuth/blob/master/docs/BitcoinAgentGateway.md",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
}
