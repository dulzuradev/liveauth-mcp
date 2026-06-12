using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Data.Entities.Mcp;
using LiveAuthCore.Models.Mcp;
using LiveAuthCore.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Controllers.Mcp;

[ApiController]
[Route("api/mcp")]
[AllowAnonymous] // Secured by PublicKeyAuthMiddleware (X-LW-Public)
public class McpGateController : ControllerBase
{
    private readonly LiveAuthDbContext _db;
    private readonly LightningService _lightning;
    private readonly LightningService _jwt;
    private readonly PowChallengeSigner _signer;
    private readonly PowDifficultyService _difficulty;
    private readonly ApiKeyService _apiKeyService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<McpGateController> _logger;
    private readonly L402Service _l402;
    private readonly McpReceiptService _receiptService;

    public McpGateController(
        LiveAuthDbContext db,
        LightningService lightning,
        LightningService jwt,
        PowChallengeSigner signer,
        PowDifficultyService difficulty,
        ApiKeyService apiKeyService,
        IConfiguration configuration,
        ILogger<McpGateController> logger,
        L402Service l402,
        McpReceiptService receiptService)
    {
        _db = db;
        _lightning = lightning;
        _jwt = jwt;
        _signer = signer;
        _difficulty = difficulty;
        _apiKeyService = apiKeyService;
        _configuration = configuration;
        _logger = logger;
        _l402 = l402;
        _receiptService = receiptService;
    }

    private Project? GetProject()
    {
        // First try to get from middleware-set item (regular API key auth)
        if (HttpContext.Items.TryGetValue("LW_Project", out var value) && value is Project proj)
            return proj;

        // Fallback to demo project if no API key provided or auth failed
        return GetDemoProjectAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    private async Task<Project?> GetDemoProjectAsync(CancellationToken ct)
    {
        var demoProjectId = _configuration["LiveAuth:DemoProjectId"];
        if (!Guid.TryParse(demoProjectId, out var projectId))
            return null;

        return await _db.Projects
            .AsNoTracking()
            .SingleOrDefaultAsync(p =>
                    p.Id == projectId &&
                    p.IsActive,
                ct);
    }

    private static string RandomHex(int bytes)
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes))
            .ToLowerInvariant();

    private static byte[] Sha256Bytes(string input)
    {
        using var sha = SHA256.Create();
        return sha.ComputeHash(Encoding.UTF8.GetBytes(input));
    }

    private static bool IsLessThan(byte[] hash, byte[] target)
    {
        for (int i = 0; i < 32; i++)
        {
            if (hash[i] < target[i]) return true;
            if (hash[i] > target[i]) return false;
        }

        return true;
    }

    private static byte[] TargetFromBits(int bits)
    {
        var target = new byte[32];

        int fullBytes = bits / 8;
        int remBits = bits % 8;

        for (int i = 0; i < fullBytes; i++)
            target[i] = 0x00;

        if (fullBytes < 32)
        {
            target[fullBytes] = (byte)(0xFF >> remBits);
            for (int i = fullBytes + 1; i < 32; i++)
                target[i] = 0xFF;
        }

        return target;
    }

    private static string BuildPowPayload(Guid projectId, string challengeHex, int difficultyBits, long expiresAtUnix)
        => $"{projectId}:{challengeHex}:{difficultyBits}:{expiresAtUnix}";

    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] McpStartRequest req, CancellationToken ct)
    {
        var project = GetProject();
        if (project == null) return Unauthorized();
        if (!project.IsActive) return Forbid();

        var mcpConfig = GetMcpConfig(project);

        var forceLightning = req.ForceLightning == true;
        var forceL402 = req.ForceL402 == true;

        // L402 bundle mode — no invoice, client must present valid macaroon on confirm
        if (forceL402)
        {
            var l402Session = new McpGateSession
            {
                ProjectId = project.Id,
                SatsPerCallAtStart = mcpConfig.SatsPerCall,
                Status = "pending",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10)
            };

            _db.McpGateSessions.Add(l402Session);
            await _db.SaveChangesAsync(ct);

            return Ok(new McpStartResponse(
                QuoteId: l402Session.Id.ToString(),
                PowChallenge: null,
                Invoice: null,
                // Hint to client: present macaroon on confirm
                AuthHint: "l402_bundle"
            ));
        }

        McpGateSession session;
        object? powChallenge = null;
        McpInvoice? invoice = null;

        if (forceLightning)
        {
            // Generate Lightning invoice
            var satsAmount = mcpConfig.SatsPerCall * mcpConfig.InvoiceCallCredits;
            
            var invoiceResult = await _lightning.CreateLoginInvoiceAsync(
                $"mcp:{project.Id}",
                satsAmount,
                10,
                project
            );

            session = new McpGateSession
            {
                ProjectId = project.Id,
                LightningInvoice = invoiceResult.Bolt11,
                LightningPaymentHash = invoiceResult.InvoiceId,
                SatsPerCallAtStart = mcpConfig.SatsPerCall,
                Status = "pending",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10)
            };

            invoice = new McpInvoice(
                Bolt11: invoiceResult.Bolt11,
                AmountSats: invoiceResult.AmountSats,
                ExpiresAtUnix: invoiceResult.ExpiresAtUnix,
                PaymentHash: invoiceResult.InvoiceId
            );
        }
        else
        {
            var difficultyBits = await _difficulty.GetDifficultyAsync(project, ct);
            var challengeHex = RandomHex(16);
            var expiresAtUnix = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds();
            var payload = BuildPowPayload(project.Id, challengeHex, difficultyBits, expiresAtUnix);
            var sig = _signer.Sign(payload);

            session = new McpGateSession
            {
                ProjectId = project.Id,
                PowChallengeHex = challengeHex,
                PowDifficultyBits = difficultyBits,
                PowExpiresAtUnix = expiresAtUnix,
                PowSignature = sig,
                SatsPerCallAtStart = mcpConfig.SatsPerCall,
                Status = "pending",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10)
            };

            var target = TargetFromBits(difficultyBits);
            powChallenge = new
            {
                projectId = project.Id,
                projectPublicKey = project.PublicKey,
                challengeHex,
                targetHex = Convert.ToHexString(target).ToLowerInvariant(),
                difficultyBits,
                expiresAtUnix,
                signature = sig
            };
        }

        _db.McpGateSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        return Ok(new McpStartResponse(
            QuoteId: session.Id.ToString(),
            PowChallenge: powChallenge,
            Invoice: invoice,
            AuthHint: null
        ));
    }

    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm([FromBody] McpConfirmRequest req, CancellationToken ct)
    {
        var project = GetProject();
        if (project == null) return Unauthorized();
        if (!project.IsActive) return Forbid();
        var mcpConfig = GetMcpConfig(project);

        if (!Guid.TryParse(req.QuoteId, out var sessionId))
            return BadRequest("Invalid quoteId");

        var session = await _db.McpGateSessions
            .Where(s => s.Id == sessionId && s.ProjectId == project.Id)
            .FirstOrDefaultAsync(ct);

        if (session == null) return NotFound();
        if (session.Status != "pending") return BadRequest("Session not pending");
        if (session.ExpiresAt < DateTime.UtcNow) return BadRequest("Session expired");

        // PoW confirm
        if (!string.IsNullOrWhiteSpace(req.ChallengeHex))
        {
            if (req.ChallengeHex != session.PowChallengeHex)
                return BadRequest("Challenge mismatch");

            if (req.DifficultyBits == null || req.ExpiresAtUnix == null || string.IsNullOrWhiteSpace(req.Sig))
                return BadRequest("Missing PoW signed fields");

            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (req.ExpiresAtUnix <= nowUnix)
                return BadRequest("Challenge expired");

            var payload = BuildPowPayload(project.Id, req.ChallengeHex, req.DifficultyBits.Value, req.ExpiresAtUnix.Value);
            if (!_signer.Verify(payload, req.Sig))
                return Unauthorized("Invalid signature");

            if (req.Nonce == null || string.IsNullOrWhiteSpace(req.HashHex))
                return BadRequest("Missing PoW solution");

            // Hash check
            var input = $"{project.PublicKey}:{req.ChallengeHex}:{req.Nonce.Value}";
            var computed = Sha256Bytes(input);
            var computedHex = Convert.ToHexString(computed).ToLowerInvariant();
            if (!computedHex.Equals(req.HashHex, StringComparison.OrdinalIgnoreCase))
                return Unauthorized("Hash mismatch");

            // Difficulty check
            var target = TargetFromBits(req.DifficultyBits.Value);
            if (!IsLessThan(computed, target))
                return Unauthorized("Difficulty not met");

            // Success → mint gate JWT (short-lived)
            session.Status = "confirmed";

            var jti = Guid.NewGuid().ToString("N");
            var expiresUtc = DateTime.UtcNow.AddMinutes(10);

            var extraClaims = new[]
            {
                new Claim("projectId", project.Id.ToString()),
                new Claim("authType", "mcp_pow"),
                new Claim("mcpQuoteId", session.Id.ToString()),
                new Claim("jti", jti)
            };

            var tokenJwt = _jwt.GenerateJwtToken(
                userId: $"mcp:{project.Id}:{session.Id}",
                role: "McpClient",
                extraClaims: extraClaims,
                expiresUtc: expiresUtc
            );

            var gateToken = new McpGateToken
            {
                ProjectId = project.Id,
                SessionId = session.Id,
                JwtId = jti,
                RefreshToken = Guid.NewGuid().ToString("N"),
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = expiresUtc,
                CallsUsed = 0,
                SatsUsed = 0,
                MaxCallsPerMinute = mcpConfig.MaxCallsPerMinute,
                MaxSatsPerDay = mcpConfig.MaxSatsPerDay,
                DayWindowStart = DateTime.UtcNow.Date,
                Status = "active"
            };

            _db.McpGateTokens.Add(gateToken);
            await _db.SaveChangesAsync(ct);

            var remaining = gateToken.MaxSatsPerDay - gateToken.SatsUsed;

            return Ok(new McpConfirmResponse(
                Jwt: tokenJwt,
                ExpiresIn: (int)TimeSpan.FromMinutes(10).TotalSeconds,
                RemainingBudgetSats: remaining,
                RefreshToken: gateToken.RefreshToken
            ));
        }

        // Lightning confirm - check if invoice is paid
        if (!string.IsNullOrWhiteSpace(session.LightningPaymentHash))
        {
            var status = await _lightning.GetInvoiceStatusAsync(session.LightningPaymentHash, project);

            if (!status.IsPaid)
            {
                return Ok(new McpConfirmResponse(
                    Jwt: null,
                    ExpiresIn: 0,
                    RemainingBudgetSats: 0,
                    PaymentStatus: "pending"
                ));
            }

            // Payment confirmed - mint JWT
            session.Status = "confirmed";

            var jti = Guid.NewGuid().ToString("N");
            var expiresUtc = DateTime.UtcNow.AddMinutes(10);

            var extraClaims = new[]
            {
                new Claim("projectId", project.Id.ToString()),
                new Claim("authType", "mcp_lightning"),
                new Claim("mcpQuoteId", session.Id.ToString()),
                new Claim("jti", jti)
            };

            var tokenJwt = _jwt.GenerateJwtToken(
                userId: $"mcp:{project.Id}:{session.Id}",
                role: "McpClient",
                extraClaims: extraClaims,
                expiresUtc: expiresUtc
            );

            var gateToken = new McpGateToken
            {
                ProjectId = project.Id,
                SessionId = session.Id,
                JwtId = jti,
                RefreshToken = Guid.NewGuid().ToString("N"),
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = expiresUtc,
                CallsUsed = 0,
                SatsUsed = 0,
                MaxCallsPerMinute = mcpConfig.MaxCallsPerMinute,
                MaxSatsPerDay = session.SatsPerCallAtStart * mcpConfig.InvoiceCallCredits,
                DayWindowStart = DateTime.UtcNow.Date,
                Status = "active"
            };

            _db.McpGateTokens.Add(gateToken);
            await _db.SaveChangesAsync(ct);

            return Ok(new McpConfirmResponse(
                Jwt: tokenJwt,
                ExpiresIn: (int)TimeSpan.FromMinutes(10).TotalSeconds,
                RemainingBudgetSats: gateToken.MaxSatsPerDay,
                PaymentStatus: "paid",
                RefreshToken: gateToken.RefreshToken
            ));
        }

        // L402 macaroon confirm — validate bundle macaroon
        if (!string.IsNullOrWhiteSpace(req.Macaroon))
        {
            var (isValid, bundleId, remainingCalls, error) = 
                await _l402.ValidateMacaroonAsync(req.Macaroon, _db);

            if (!isValid)
            {
                return StatusCode(402, new
                {
                    error = "Payment required",
                    message = error ?? "Invalid or depleted macaroon"
                });
            }

            // Macaroon valid — mint JWT for the session
            session.Status = "confirmed";

            var jti = Guid.NewGuid().ToString("N");
            var expiresUtc = DateTime.UtcNow.AddMinutes(10);

            var extraClaims = new[]
            {
                new Claim("projectId", project.Id.ToString()),
                new Claim("authType", "mcp_l402"),
                new Claim("mcpQuoteId", session.Id.ToString()),
                new Claim("jti", jti),
                new Claim("bundleId", bundleId ?? "")
            };

            var tokenJwt = _jwt.GenerateJwtToken(
                userId: $"mcp:{project.Id}:{session.Id}",
                role: "McpClient",
                extraClaims: extraClaims,
                expiresUtc: expiresUtc
            );

            var gateToken = new McpGateToken
            {
                ProjectId = project.Id,
                SessionId = session.Id,
                JwtId = jti,
                RefreshToken = Guid.NewGuid().ToString("N"),
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = expiresUtc,
                CallsUsed = 0,
                SatsUsed = 0,
                MaxCallsPerMinute = mcpConfig.MaxCallsPerMinute,
                MaxSatsPerDay = remainingCalls * 1, // 1 sat per call budget
                DayWindowStart = DateTime.UtcNow.Date,
                Status = "active"
            };

            _db.McpGateTokens.Add(gateToken);
            await _db.SaveChangesAsync(ct);

            return Ok(new McpConfirmResponse(
                Jwt: tokenJwt,
                ExpiresIn: (int)TimeSpan.FromMinutes(10).TotalSeconds,
                RemainingBudgetSats: remainingCalls,
                PaymentStatus: "l402_paid",
                RefreshToken: gateToken.RefreshToken
            ));
        }

        return BadRequest("No valid confirmation method provided");
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] McpRefreshRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.RefreshToken))
            return BadRequest("Refresh token required");

        var gateToken = await _db.McpGateTokens
            .Where(t => t.RefreshToken == req.RefreshToken && t.Status == "active")
            .FirstOrDefaultAsync(ct);

        if (gateToken == null)
            return Unauthorized("Invalid refresh token");

        // Get project for JWT generation
        var project = await _db.Projects
            .Where(p => p.Id == gateToken.ProjectId && p.IsActive)
            .FirstOrDefaultAsync(ct);

        if (project == null)
            return Forbid();

        // Generate new JWT
        var newJti = Guid.NewGuid().ToString("N");
        var expiresUtc = DateTime.UtcNow.AddMinutes(10);

        var extraClaims = new[]
        {
            new Claim("projectId", project.Id.ToString()),
            new Claim("authType", "mcp_refresh"),
            new Claim("mcpQuoteId", gateToken.SessionId.ToString()),
            new Claim("jti", newJti)
        };

        var tokenJwt = _jwt.GenerateJwtToken(
            userId: $"mcp:{project.Id}:{gateToken.SessionId}",
            role: "McpClient",
            extraClaims: extraClaims,
            expiresUtc: expiresUtc
        );

        // Update token record
        gateToken.JwtId = newJti;
        gateToken.ExpiresAt = expiresUtc;
        await _db.SaveChangesAsync(ct);

        var remaining = gateToken.MaxSatsPerDay - gateToken.SatsUsed;

        return Ok(new McpRefreshResponse(
            Jwt: tokenJwt,
            ExpiresIn: (int)TimeSpan.FromMinutes(10).TotalSeconds,
            RemainingBudgetSats: remaining
        ));
    }

    /// <summary>
    /// LNURL-compatible endpoint for lnget. Returns the Lightning invoice for polling.
    /// GET /api/mcp/lnurl/{quoteId}
    /// </summary>
    [HttpGet("lnurl/{quoteId}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetLnurl(string quoteId, CancellationToken ct)
    {
        var project = GetProject();
        if (project == null) return Unauthorized();
        if (!project.IsActive) return Forbid();

        if (!Guid.TryParse(quoteId, out var sessionId))
            return BadRequest("Invalid quoteId");

        var session = await _db.McpGateSessions
            .Where(s => s.Id == sessionId && s.ProjectId == project.Id)
            .FirstOrDefaultAsync(ct);

        if (session == null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(session.LightningInvoice))
            return BadRequest("No Lightning invoice for this session");

        // Return lnget-compatible format
        return Ok(new
        {
            pr = session.LightningInvoice,
            routes = Array.Empty<string>()
        });
    }

    [HttpGet("status/{quoteId}")]
    public async Task<IActionResult> GetStatus(string quoteId, CancellationToken ct)
    {
        var project = GetProject();
        if (project == null) return Unauthorized();
        if (!project.IsActive) return Forbid();

        if (!Guid.TryParse(quoteId, out var sessionId))
            return BadRequest("Invalid quoteId");

        var session = await _db.McpGateSessions
            .Where(s => s.Id == sessionId && s.ProjectId == project.Id)
            .FirstOrDefaultAsync(ct);

        if (session == null)
            return NotFound();

        // If Lightning, check payment status
        string? paymentStatus = null;
        if (!string.IsNullOrWhiteSpace(session.LightningPaymentHash))
        {
            var status = await _lightning.GetInvoiceStatusAsync(session.LightningPaymentHash, project);
            paymentStatus = status.IsPaid ? "paid" : "pending";
        }

        return Ok(new
        {
            quoteId = session.Id.ToString(),
            status = session.Status,
            paymentStatus,
            expiresAt = session.ExpiresAt
        });
    }

    [HttpPost("charge")]
    [Authorize] // requires JWT
    public async Task<IActionResult> Charge([FromBody] McpChargeRequest req, CancellationToken ct)
    {
        var resolved = await TryGetChargeContextAsync(ct);
        if (resolved.Error != null)
            return resolved.Error;

        var context = resolved.Context!;
        var toolResult = await ResolveToolForChargeAsync(req, ct);
        if (toolResult.Error != null)
            return toolResult.Error;

        if (toolResult.Tool != null)
            return await ChargeResolvedToolAsync(toolResult.Tool, req, context, ct);

        var mcpConfig = GetMcpConfig(context.Project);
        var callCostSats = req.CallCostSats ?? mcpConfig.SatsPerCall;
        if (callCostSats <= 0) return BadRequest("callCostSats must be positive");

        var budgetResult = ApplyBudgetCharge(context.GateToken, context.Project, callCostSats);
        if (budgetResult.Status != "ok")
            return Ok(budgetResult);
        
        await _db.SaveChangesAsync(ct);

        return Ok(budgetResult);
    }

    [HttpPost("tools/{toolId:guid}/charge")]
    [Authorize]
    public async Task<IActionResult> ChargeTool(Guid toolId, [FromBody] McpChargeRequest req, CancellationToken ct)
    {
        var tool = await _db.McpTools
            .Where(t => t.Id == toolId && t.RemovedAt == null)
            .FirstOrDefaultAsync(ct);

        if (tool == null)
            return NotFound("Unknown MCP tool");

        var resolved = await TryGetChargeContextAsync(ct);
        if (resolved.Error != null)
            return resolved.Error;

        return await ChargeResolvedToolAsync(tool, req, resolved.Context!, ct);
    }

    private async Task<IActionResult> ChargeResolvedToolAsync(
        McpTool tool,
        McpChargeRequest req,
        McpChargeContext context,
        CancellationToken ct)
    {
        var gateToken = context.GateToken;
        var project = context.Project;
        var callCostSats = req.CallCostSats ?? Math.Clamp(tool.DefaultCostSats, 1, int.MaxValue);

        if (!string.Equals(tool.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            await RecordDeniedToolChargeAsync(tool, req, context, callCostSats, "tool_inactive", ct);
            return Ok(new McpChargeResponse(
                "deny",
                gateToken.CallsUsed,
                gateToken.SatsUsed,
                Reason: "tool_inactive",
                ToolId: tool.Id,
                ToolName: tool.Name,
                ToolSlug: tool.Slug));
        }

        if (callCostSats < tool.MinCostSats)
            return BadRequest($"callCostSats must be at least {tool.MinCostSats} for this tool");

        if (tool.MaxCostSats > 0 && callCostSats > tool.MaxCostSats)
            return BadRequest($"callCostSats must be no more than {tool.MaxCostSats} for this tool");

        var idempotencyKey = string.IsNullOrWhiteSpace(req.IdempotencyKey)
            ? null
            : req.IdempotencyKey.Trim();

        if (idempotencyKey != null)
        {
            var existing = await _db.McpToolRevenueEvents
                .Where(e => e.McpToolId == tool.Id && e.IdempotencyKey == idempotencyKey)
                .FirstOrDefaultAsync(ct);

            if (existing != null)
            {
                return Ok(new McpChargeResponse(
                    "ok",
                    gateToken.CallsUsed,
                    gateToken.SatsUsed,
                    existing.GrossSats,
                    existing.PlatformFeeSats,
                    existing.NetSats,
                    existing.FeeBasisPoints,
                    existing.Id,
                    Receipt: _receiptService.CreateReceipt(existing, tool),
                    ToolId: tool.Id,
                    ToolName: tool.Name,
                    ToolSlug: tool.Slug));
            }
        }

        var budgetResult = ApplyBudgetCharge(gateToken, project, callCostSats);
        if (budgetResult.Status != "ok")
        {
            await RecordDeniedToolChargeAsync(tool, req, context, callCostSats, budgetResult.Reason ?? "charge_denied", ct);
            return Ok(new McpChargeResponse(
                budgetResult.Status,
                budgetResult.CallsUsed,
                budgetResult.SatsUsed,
                Reason: budgetResult.Reason,
                ToolId: tool.Id,
                ToolName: tool.Name,
                ToolSlug: tool.Slug));
        }

        var fee = CalculatePlatformFee(callCostSats);
        var metadataJson = CreateMetadataJson(req);

        var revenueEvent = new McpToolRevenueEvent
        {
            McpToolId = tool.Id,
            McpGateTokenId = gateToken.Id,
            McpGateSessionId = gateToken.SessionId,
            PayingProjectId = project.Id,
            AgentId = string.IsNullOrWhiteSpace(req.AgentId) ? null : req.AgentId.Trim(),
            ToolMethodName = string.IsNullOrWhiteSpace(req.ToolMethodName)
                ? tool.Slug
                : req.ToolMethodName.Trim(),
            GrossSats = callCostSats,
            PlatformFeeSats = fee.PlatformFeeSats,
            NetSats = fee.NetSats,
            FeeBasisPoints = fee.FeeBasisPoints,
            Status = "Charged",
            IdempotencyKey = idempotencyKey,
            RequestId = HttpContext.TraceIdentifier,
            MetadataJson = metadataJson,
            CreatedAt = DateTime.UtcNow
        };

        _db.McpToolRevenueEvents.Add(revenueEvent);
        await _db.SaveChangesAsync(ct);

        return Ok(new McpChargeResponse(
            "ok",
            gateToken.CallsUsed,
            gateToken.SatsUsed,
            revenueEvent.GrossSats,
            revenueEvent.PlatformFeeSats,
            revenueEvent.NetSats,
            revenueEvent.FeeBasisPoints,
            revenueEvent.Id,
            Receipt: _receiptService.CreateReceipt(revenueEvent, tool),
            ToolId: tool.Id,
            ToolName: tool.Name,
            ToolSlug: tool.Slug));
    }

    [HttpGet("usage")]
    [Authorize]
    public async Task<IActionResult> GetUsage(CancellationToken ct)
    {
        var projectIdStr = User.FindFirst("projectId")?.Value;
        var jti = User.FindFirst("jti")?.Value;

        if (string.IsNullOrWhiteSpace(projectIdStr) || string.IsNullOrWhiteSpace(jti))
            return Unauthorized("Missing projectId/jti");

        if (!Guid.TryParse(projectIdStr, out var projectId))
            return Unauthorized("Invalid projectId");

        var gateToken = await _db.McpGateTokens
            .Where(t => t.ProjectId == projectId && t.JwtId == jti && t.Status == "active")
            .FirstOrDefaultAsync(ct);

        if (gateToken == null)
            return NotFound("No active session found");

        // Reset day window if needed
        if (gateToken.DayWindowStart.Date != DateTime.UtcNow.Date)
        {
            gateToken.DayWindowStart = DateTime.UtcNow.Date;
            gateToken.SatsUsed = 0;
            gateToken.CallsUsed = 0;
            await _db.SaveChangesAsync(ct);
        }

        var remaining = gateToken.MaxSatsPerDay - gateToken.SatsUsed;

        return Ok(new McpUsageResponse(
            Status: gateToken.Status,
            CallsUsed: gateToken.CallsUsed,
            SatsUsed: gateToken.SatsUsed,
            MaxSatsPerDay: gateToken.MaxSatsPerDay,
            RemainingBudgetSats: remaining,
            MaxCallsPerMinute: gateToken.MaxCallsPerMinute,
            ExpiresAt: gateToken.ExpiresAt,
            DayWindowStart: gateToken.DayWindowStart
        ));
    }

    private async Task<(McpTool? Tool, IActionResult? Error)> ResolveToolForChargeAsync(
        McpChargeRequest req,
        CancellationToken ct)
    {
        if (req.ToolId.HasValue)
        {
            var toolById = await _db.McpTools
                .Where(t => t.Id == req.ToolId.Value && t.RemovedAt == null)
                .FirstOrDefaultAsync(ct);

            return toolById == null
                ? (null, NotFound("Unknown MCP tool"))
                : (toolById, null);
        }

        var toolName = req.ToolName?.Trim();
        if (string.IsNullOrWhiteSpace(toolName))
            return (null, null);

        var normalizedToolName = toolName.ToLowerInvariant();
        var toolBySlug = await _db.McpTools
            .Where(t => t.RemovedAt == null && t.Slug.ToLower() == normalizedToolName)
            .FirstOrDefaultAsync(ct);

        if (toolBySlug != null)
            return (toolBySlug, null);

        var toolsByName = await _db.McpTools
            .Where(t => t.RemovedAt == null && t.Name.ToLower() == normalizedToolName)
            .Take(2)
            .ToListAsync(ct);

        return toolsByName.Count switch
        {
            0 => (null, NotFound("Unknown MCP tool")),
            1 => (toolsByName[0], null),
            _ => (null, BadRequest("toolName is ambiguous; use toolId or the tool slug."))
        };
    }

    private async Task RecordDeniedToolChargeAsync(
        McpTool tool,
        McpChargeRequest req,
        McpChargeContext context,
        int callCostSats,
        string reason,
        CancellationToken ct)
    {
        _db.McpToolRevenueEvents.Add(new McpToolRevenueEvent
        {
            McpToolId = tool.Id,
            McpGateTokenId = context.GateToken.Id,
            McpGateSessionId = context.GateToken.SessionId,
            PayingProjectId = context.Project.Id,
            AgentId = string.IsNullOrWhiteSpace(req.AgentId) ? null : req.AgentId.Trim(),
            ToolMethodName = string.IsNullOrWhiteSpace(req.ToolMethodName)
                ? tool.Slug
                : req.ToolMethodName.Trim(),
            GrossSats = callCostSats,
            PlatformFeeSats = 0,
            NetSats = 0,
            FeeBasisPoints = 0,
            Status = "Denied",
            RequestId = HttpContext.TraceIdentifier,
            MetadataJson = CreateMetadataJson(req, reason),
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);
    }

    private static string? CreateMetadataJson(McpChargeRequest req, string? denyReason = null)
    {
        if (string.IsNullOrWhiteSpace(denyReason))
            return req.Metadata.HasValue ? JsonSerializer.Serialize(req.Metadata.Value) : null;

        return req.Metadata.HasValue
            ? JsonSerializer.Serialize(new { denyReason, metadata = req.Metadata.Value })
            : JsonSerializer.Serialize(new { denyReason });
    }

    private static McpProjectConfig GetMcpConfig(Project project)
    {
        return new McpProjectConfig(
            SatsPerCall: Math.Clamp(project.McpSatsPerCall, 1, 10_000),
            InvoiceCallCredits: Math.Clamp(project.McpInvoiceCallCredits, 1, 10_000),
            MaxSatsPerDay: Math.Clamp(project.McpMaxSatsPerDay, 1, 10_000_000),
            MaxCallsPerMinute: Math.Clamp(project.McpMaxCallsPerMinute, 1, 10_000)
        );
    }

    private async Task<(McpChargeContext? Context, IActionResult? Error)> TryGetChargeContextAsync(CancellationToken ct)
    {
        var projectIdStr = User.FindFirst("projectId")?.Value;
        var jti = User.FindFirst("jti")?.Value;

        if (string.IsNullOrWhiteSpace(projectIdStr) || string.IsNullOrWhiteSpace(jti))
            return (null, Unauthorized("Missing projectId/jti"));

        if (!Guid.TryParse(projectIdStr, out var projectId))
            return (null, Unauthorized("Invalid projectId"));

        var gateToken = await _db.McpGateTokens
            .Where(t => t.ProjectId == projectId && t.JwtId == jti && t.Status == "active")
            .FirstOrDefaultAsync(ct);

        if (gateToken == null) return (null, Unauthorized("Unknown token"));
        if (gateToken.ExpiresAt < DateTime.UtcNow) return (null, Unauthorized("Token expired"));

        var project = await _db.Projects
            .Where(p => p.Id == gateToken.ProjectId && p.IsActive)
            .FirstOrDefaultAsync(ct);

        if (project == null) return (null, Forbid("Project not active"));

        if (gateToken.DayWindowStart.Date != DateTime.UtcNow.Date)
        {
            gateToken.DayWindowStart = DateTime.UtcNow.Date;
            gateToken.SatsUsed = 0;
            gateToken.CallsUsed = 0;
        }

        return (new McpChargeContext(gateToken, project), null);
    }

    private static McpChargeResponse ApplyBudgetCharge(McpGateToken gateToken, Project project, int callCostSats)
    {
        if (project.L402BalanceSats >= callCostSats)
        {
            project.L402BalanceSats -= callCostSats;
            gateToken.CallsUsed += 1;
            gateToken.SatsUsed += callCostSats;
            return new McpChargeResponse("ok", gateToken.CallsUsed, gateToken.SatsUsed);
        }

        if (gateToken.SatsUsed + callCostSats > gateToken.MaxSatsPerDay)
        {
            return new McpChargeResponse(
                "deny",
                gateToken.CallsUsed,
                gateToken.SatsUsed,
                Reason: "budget_exceeded");
        }

        gateToken.CallsUsed += 1;
        gateToken.SatsUsed += callCostSats;

        return new McpChargeResponse("ok", gateToken.CallsUsed, gateToken.SatsUsed);
    }

    private static (int PlatformFeeSats, int NetSats, int FeeBasisPoints) CalculatePlatformFee(int grossSats)
    {
        const int feeBasisPoints = 500;
        var platformFeeSats = grossSats > 0
            ? Math.Max(1, grossSats * feeBasisPoints / 10_000)
            : 0;

        return (platformFeeSats, grossSats - platformFeeSats, feeBasisPoints);
    }

    private readonly record struct McpProjectConfig(
        int SatsPerCall,
        int InvoiceCallCredits,
        long MaxSatsPerDay,
        int MaxCallsPerMinute);

    private sealed record McpChargeContext(McpGateToken GateToken, Project Project);
}
