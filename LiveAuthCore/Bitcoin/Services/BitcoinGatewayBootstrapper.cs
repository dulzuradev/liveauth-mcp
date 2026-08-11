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
                _options.Tools.TransactionStatus.PriceSats, projectId)
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
            existing.DefaultCostSats = configured.DefaultCostSats;
            existing.MinCostSats = configured.DefaultCostSats;
            existing.MaxCostSats = configured.DefaultCostSats;
            existing.Status = "Active";
            existing.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
    }

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
