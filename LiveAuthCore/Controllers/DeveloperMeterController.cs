using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Models.Meter;
using LiveAuthCore.Services;
using LiveAuthCore.Services.Meter;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Controllers;

[ApiController]
[Route("api/dev/projects/{projectId:guid}/meter")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class DeveloperMeterController : ControllerBase
{
    private readonly LiveAuthDbContext _db;
    private readonly IMeterRouteMatcher _routes;
    private readonly IMeterSsrfGuard _ssrf;
    private readonly IMeterSecretProtector _secrets;
    private readonly ILightningInvoiceProviderFactory _providers;
    private readonly IMeterReceiptService _receipts;
    private readonly WebhookService _webhooks;

    public DeveloperMeterController(LiveAuthDbContext db, IMeterRouteMatcher routes, IMeterSsrfGuard ssrf,
        IMeterSecretProtector secrets, ILightningInvoiceProviderFactory providers,
        IMeterReceiptService receipts, WebhookService webhooks)
    {
        _db = db; _routes = routes; _ssrf = ssrf; _secrets = secrets;
        _providers = providers; _receipts = receipts; _webhooks = webhooks;
    }

    [HttpGet]
    public async Task<ActionResult<MeterSettingsDto>> Get(Guid projectId, CancellationToken ct)
    {
        var project = await OwnedProject(projectId, ct);
        if (project == null) return NotFound();
        var settings = await GetOrCreateSettings(project, ct);
        return Ok(ToDto(settings));
    }

    [HttpPut]
    public async Task<ActionResult<MeterSettingsDto>> Put(Guid projectId, UpdateMeterSettingsRequest request, CancellationToken ct)
    {
        var project = await OwnedProject(projectId, ct);
        if (project == null) return NotFound();
        var environment = request.Environment.Trim().ToUpperInvariant();
        if (environment is not (MeterEnvironments.Test or MeterEnvironments.Live))
            return Validation("environment", "Environment must be TEST or LIVE.");
        var behavior = request.UnmatchedRouteBehavior.Trim().ToUpperInvariant();
        if (behavior is not (MeterUnmatchedRouteBehaviors.Free or MeterUnmatchedRouteBehaviors.Block or MeterUnmatchedRouteBehaviors.DefaultPrice))
            return Validation("unmatchedRouteBehavior", "Unmatched behavior must be FREE, BLOCK, or DEFAULT_PRICE.");
        if (request.DefaultPriceSats < 0 || request.MonthlyFreeRequestAllowance < 0)
            return Validation("pricing", "Prices and allowances cannot be negative.");
        if (request.OriginTimeoutSeconds is < 1 or > 120 || request.MaximumRequestBodyBytes is < 1 or > 52_428_800 ||
            request.MaximumResponseBodyBytes is < 1 or > 104_857_600)
            return Validation("limits", "Timeout or body limits are outside the supported range.");

        var settings = await GetOrCreateSettings(project, ct);
        if (!string.IsNullOrWhiteSpace(request.OriginBaseUrl))
        {
            try
            {
                await _ssrf.ValidateAndResolveAsync(request.OriginBaseUrl,
                    environment == MeterEnvironments.Live,
                    environment == MeterEnvironments.Test && request.AllowPrivateOriginInTest, ct);
            }
            catch (MeterSecurityException ex) { return Validation("originBaseUrl", ex.Message); }
        }
        else if (request.Enabled) return Validation("originBaseUrl", "An origin is required before Meter can be enabled.");

        if (!TryHostname(request.PublicGatewayHostname, out var hostname))
            return Validation("publicGatewayHostname", "Gateway hostname must be a DNS hostname without a scheme or path.");
        if (!TryWebhook(request.WebhookUrl, environment == MeterEnvironments.Live, out var webhook))
            return Validation("webhookUrl", "Webhook must be an absolute HTTPS URL in LIVE.");
        if (webhook != null)
        {
            try { await _ssrf.ValidateAndResolveAsync(webhook, environment == MeterEnvironments.Live, false, ct); }
            catch (MeterSecurityException ex) { return Validation("webhookUrl", ex.Message); }
        }
        if (request.Enabled && environment == MeterEnvironments.Live && settings.LightningConnectionId == null)
            return Validation("lightningConnection", "A merchant-controlled Lightning connection is required in LIVE.");

        settings.Enabled = request.Enabled;
        settings.OriginBaseUrl = request.OriginBaseUrl?.Trim().TrimEnd('/');
        settings.Environment = environment;
        settings.PublicGatewayHostname = hostname;
        settings.OriginTimeoutSeconds = request.OriginTimeoutSeconds;
        settings.MonthlyFreeRequestAllowance = request.MonthlyFreeRequestAllowance;
        settings.DefaultPriceSats = request.DefaultPriceSats;
        settings.UnmatchedRouteBehavior = behavior;
        settings.ReceiptSigningEnabled = request.ReceiptSigningEnabled;
        settings.WebhookUrl = webhook;
        settings.AllowPrivateOriginInTest = environment == MeterEnvironments.Test && request.AllowPrivateOriginInTest;
        settings.MaximumRequestBodyBytes = request.MaximumRequestBodyBytes;
        settings.MaximumResponseBodyBytes = request.MaximumResponseBodyBytes;
        settings.UpdatedAt = DateTime.UtcNow;
        Audit(projectId, "CONFIG_CHANGED", "meter_settings_updated");
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Conflict(new { error = "gateway_hostname_conflict", message = "That gateway hostname is already assigned." }); }
        return Ok(ToDto(settings));
    }

    [HttpGet("routes")]
    public async Task<ActionResult<IReadOnlyList<MeterRouteRuleDto>>> ListRoutes(Guid projectId, CancellationToken ct)
    {
        if (await OwnedProject(projectId, ct) == null) return NotFound();
        var routes = await _db.MeterRouteRules.Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.Priority).ThenBy(x => x.Id).ToListAsync(ct);
        return Ok(routes.Select(ToDto));
    }

    [HttpPost("routes")]
    public async Task<ActionResult<MeterRouteRuleDto>> CreateRoute(Guid projectId,
        UpsertMeterRouteRuleRequest request, CancellationToken ct)
    {
        if (await OwnedProject(projectId, ct) == null) return NotFound();
        var error = ValidateRoute(request, out var method);
        if (error != null) return Validation(error.Value.Key, error.Value.Value);
        var route = new MeterRouteRule { ProjectId = projectId };
        Apply(route, request, method!);
        _db.MeterRouteRules.Add(route);
        Audit(projectId, "CONFIG_CHANGED", "meter_route_created");
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(ListRoutes), new { projectId }, ToDto(route));
    }

    [HttpPut("routes/{routeId:guid}")]
    public async Task<ActionResult<MeterRouteRuleDto>> UpdateRoute(Guid projectId, Guid routeId,
        UpsertMeterRouteRuleRequest request, CancellationToken ct)
    {
        if (await OwnedProject(projectId, ct) == null) return NotFound();
        var route = await _db.MeterRouteRules.SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == routeId, ct);
        if (route == null) return NotFound();
        var error = ValidateRoute(request, out var method);
        if (error != null) return Validation(error.Value.Key, error.Value.Value);
        Apply(route, request, method!);
        Audit(projectId, "CONFIG_CHANGED", "meter_route_updated");
        await _db.SaveChangesAsync(ct);
        return Ok(ToDto(route));
    }

    [HttpDelete("routes/{routeId:guid}")]
    public async Task<IActionResult> DeleteRoute(Guid projectId, Guid routeId, CancellationToken ct)
    {
        if (await OwnedProject(projectId, ct) == null) return NotFound();
        var route = await _db.MeterRouteRules.SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == routeId, ct);
        if (route == null) return NotFound();
        var referenced = await _db.MeterPaymentChallenges.AnyAsync(x => x.RouteRuleId == routeId, ct);
        if (referenced) { route.Enabled = false; route.UpdatedAt = DateTime.UtcNow; }
        else _db.MeterRouteRules.Remove(route);
        Audit(projectId, "CONFIG_CHANGED", referenced ? "meter_route_disabled" : "meter_route_deleted");
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("lightning")]
    public async Task<ActionResult<MeterLightningConnectionDto?>> GetLightning(Guid projectId, CancellationToken ct)
    {
        if (await OwnedProject(projectId, ct) == null) return NotFound();
        var connection = await _db.MerchantLightningConnections.AsNoTracking()
            .OrderByDescending(x => x.UpdatedAt).FirstOrDefaultAsync(x => x.ProjectId == projectId, ct);
        return Ok(connection == null ? null : ToDto(connection));
    }

    [HttpPut("lightning")]
    public async Task<ActionResult<MeterLightningConnectionDto>> PutLightning(Guid projectId,
        UpsertMeterLightningConnectionRequest request, CancellationToken ct)
    {
        var project = await OwnedProject(projectId, ct);
        if (project == null) return NotFound();
        if (!string.Equals(request.ProviderType, "LND_REST", StringComparison.OrdinalIgnoreCase))
            return Validation("providerType", "The MVP supports LND_REST.");
        if (!Uri.TryCreate(request.RestUrl, UriKind.Absolute, out var rest) ||
            rest.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(rest.UserInfo))
            return Validation("restUrl", "LND REST URL is invalid.");
        var settings = await GetOrCreateSettings(project, ct);
        if (settings.Environment == MeterEnvironments.Live && rest.Scheme != "https")
            return Validation("restUrl", "LND REST must use HTTPS in LIVE.");

        var connection = await _db.MerchantLightningConnections.FirstOrDefaultAsync(x => x.ProjectId == projectId, ct);
        if (connection == null)
        {
            if (string.IsNullOrWhiteSpace(request.Macaroon)) return Validation("macaroon", "An invoice macaroon is required.");
            connection = new MerchantLightningConnection { ProjectId = projectId };
            _db.MerchantLightningConnections.Add(connection);
        }
        connection.ProviderType = "LND_REST";
        connection.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? "Merchant LND" : request.DisplayName.Trim()[..Math.Min(request.DisplayName.Trim().Length, 120)];
        connection.RestUrl = request.RestUrl.Trim().TrimEnd('/');
        connection.SupportsPaymentLookup = request.SupportsPaymentLookup;
        if (!string.IsNullOrWhiteSpace(request.Macaroon)) connection.EncryptedMacaroon = _secrets.Protect(request.Macaroon.Trim());
        if (!string.IsNullOrWhiteSpace(request.TlsCertificate)) connection.EncryptedTlsCertificate = _secrets.Protect(request.TlsCertificate.Trim());
        connection.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        settings.LightningConnectionId = connection.Id;
        settings.LightningConnection = connection;
        Audit(projectId, "CONFIG_CHANGED", "meter_lightning_updated");
        await _db.SaveChangesAsync(ct);
        return Ok(ToDto(connection));
    }

    [HttpPost("lightning/test")]
    public async Task<ActionResult<MeterLightningTestResponse>> TestLightning(Guid projectId, CancellationToken ct)
    {
        if (await OwnedProject(projectId, ct) == null) return NotFound();
        var connection = await _db.MerchantLightningConnections.SingleOrDefaultAsync(x => x.ProjectId == projectId, ct);
        if (connection == null) return BadRequest(new { error = "lightning_not_configured" });
        var status = await _providers.Get(connection.ProviderType).ValidateConnectionAsync(connection, ct);
        if (status.Success) { connection.LastValidatedAt = DateTime.UtcNow; await _db.SaveChangesAsync(ct); }
        return Ok(new MeterLightningTestResponse(status.Success, status.Alias, status.Version, status.Error));
    }

    [HttpGet("receipts")]
    public async Task<ActionResult<IReadOnlyList<MeterReceiptDto>>> ListReceipts(Guid projectId,
        [FromQuery] int limit = 50, CancellationToken ct = default)
    {
        if (await OwnedProject(projectId, ct) == null) return NotFound();
        var receipts = await _db.MeterReceipts.AsNoTracking().Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.CreatedAt).Take(Math.Clamp(limit, 1, 200)).ToListAsync(ct);
        return Ok(receipts.Select(ToDto));
    }

    [HttpGet("receipts/{receiptId:guid}")]
    public async Task<ActionResult<MeterReceiptDto>> GetReceipt(Guid projectId, Guid receiptId, CancellationToken ct)
    {
        if (await OwnedProject(projectId, ct) == null) return NotFound();
        var receipt = await _db.MeterReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == receiptId, ct);
        return receipt == null ? NotFound() : Ok(ToDto(receipt));
    }

    [HttpGet("analytics")]
    public async Task<ActionResult<MeterAnalyticsDto>> Analytics(Guid projectId,
        [FromQuery] int windowHours = 24, CancellationToken ct = default)
    {
        if (await OwnedProject(projectId, ct) == null) return NotFound();
        var end = DateTime.UtcNow; var start = end.AddHours(-Math.Clamp(windowHours, 1, 24 * 31));
        var events = await _db.MeterUsageEvents.AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.CreatedAt >= start).ToListAsync(ct);
        var requests = events.Where(x => x.Kind is "FREE" or "PAID" or "DENIED" or "ORIGIN_ERROR").ToList();
        var paid = events.Where(x => x.Kind == "PAID").ToList();
        var challenges = events.LongCount(x => x.Kind == "CHALLENGE");
        var top = requests.GroupBy(x => x.NormalizedRoute).Select(g => new MeterRouteAnalyticsDto(g.Key,
            g.LongCount(), g.LongCount(x => x.Kind == "PAID"), g.Where(x => x.Kind == "PAID").Sum(x => x.AmountSats)))
            .OrderByDescending(x => x.Requests).Take(10).ToList();
        var recent = paid.OrderByDescending(x => x.CreatedAt).Take(20).Select(x => new MeterRecentPaidRequestDto(
            x.CreatedAt, x.HttpMethod, x.NormalizedRoute, x.AmountSats, x.OriginStatusCode, x.CorrelationId, x.ChallengeId)).ToList();
        var total = requests.Count;
        return Ok(new MeterAnalyticsDto(start, end, total, events.LongCount(x => x.Kind == "FREE"), paid.Count,
            challenges, challenges == 0 ? 0 : paid.Count * 100d / challenges, paid.Sum(x => x.AmountSats),
            paid.Count == 0 ? 0 : paid.Average(x => x.AmountSats),
            total == 0 ? 0 : requests.Count(x => x.Kind == "DENIED") * 100d / total,
            total == 0 ? 0 : requests.Count(x => x.Kind == "ORIGIN_ERROR" || x.OriginStatusCode >= 500) * 100d / total,
            total == 0 ? 0 : requests.Average(x => x.GatewayLatencyMilliseconds), top, recent));
    }

    [HttpPost("webhooks/test")]
    public async Task<IActionResult> TestWebhook(Guid projectId, CancellationToken ct)
    {
        var project = await OwnedProject(projectId, ct);
        if (project == null) return NotFound();
        var settings = await GetOrCreateSettings(project, ct);
        if (string.IsNullOrWhiteSpace(settings.WebhookUrl)) return BadRequest(new { error = "webhook_not_configured" });
        var eventId = Guid.NewGuid();
        await _webhooks.EnqueueWithIdAsync(project, "meter.webhook.test", new
        { eventId, projectId, createdAt = DateTime.UtcNow }, settings.WebhookUrl, eventId, ct);
        return Accepted(new { eventId });
    }

    private async Task<Project?> OwnedProject(Guid id, CancellationToken ct)
    {
        var developerId = GetDeveloperId();
        return await _db.Projects.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted &&
            (User.IsInRole("Admin") || x.DeveloperId == developerId), ct);
    }

    private Guid GetDeveloperId()
    {
        var value = User.FindFirst("userId")?.Value ?? User.FindFirst("developer_id")?.Value ??
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return Guid.TryParse(value, out var id) ? id : throw new UnauthorizedAccessException("Invalid developer identity.");
    }

    private async Task<MeterProjectSettings> GetOrCreateSettings(Project project, CancellationToken ct)
    {
        var settings = await _db.MeterProjectSettings.Include(x => x.LightningConnection)
            .SingleOrDefaultAsync(x => x.ProjectId == project.Id, ct);
        if (settings != null) { settings.Project = project; return settings; }
        settings = new MeterProjectSettings { ProjectId = project.Id, Project = project, Environment = project.Environment };
        _db.MeterProjectSettings.Add(settings);
        await _db.SaveChangesAsync(ct);
        return settings;
    }

    private KeyValuePair<string, string>? ValidateRoute(UpsertMeterRouteRuleRequest request, out string? method)
    {
        method = request.HttpMethod.Trim().ToUpperInvariant();
        var validMethods = new[] { "*", "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS" };
        if (!validMethods.Contains(method)) return new("httpMethod", "HTTP method is not supported.");
        var patternError = _routes.ValidatePattern(request.PathPattern);
        if (patternError != null) return new("pathPattern", patternError);
        if (request.PriceSats < 0 || request.PriceSats > 100_000_000 || request.FreeRequestAllowance < 0)
            return new("priceSats", "Price or allowance is outside the supported range.");
        if (request.CredentialLifetimeSeconds is < 60 or > 86400 || request.MaximumCredentialUses is < 1 or > 10000)
            return new("credential", "Credential lifetime or maximum uses is outside the supported range.");
        if (request.BindRequestBody && (request.MaximumCredentialUses ?? 1) != 1)
            return new("bindRequestBody", "Body-bound credentials must be one-shot.");
        return null;
    }

    private static void Apply(MeterRouteRule route, UpsertMeterRouteRuleRequest request, string method)
    {
        route.HttpMethod = method; route.PathPattern = MeterRouteMatcher.NormalizePath(request.PathPattern);
        route.PriceSats = request.PriceSats; route.FreeRequestAllowance = request.FreeRequestAllowance;
        route.Enabled = request.Enabled; route.Priority = request.Priority;
        route.CredentialLifetimeSeconds = request.CredentialLifetimeSeconds;
        route.MaximumCredentialUses = request.MaximumCredentialUses;
        route.BindRequestBody = request.BindRequestBody; route.UpdatedAt = DateTime.UtcNow;
    }

    private void Audit(Guid projectId, string kind, string code) => _db.MeterUsageEvents.Add(new MeterUsageEvent
    { ProjectId = projectId, Environment = "CONTROL", Kind = kind, HttpMethod = "CONTROL", Path = "/meter",
      NormalizedRoute = "/meter", CorrelationId = HttpContext.TraceIdentifier, CallerKey = GetDeveloperId().ToString("N"),
      ErrorCode = code, CreatedAt = DateTime.UtcNow });

    private static bool TryHostname(string? value, out string? hostname)
    {
        hostname = string.IsNullOrWhiteSpace(value) ? null : value.Trim().TrimEnd('.').ToLowerInvariant();
        return hostname == null || (hostname.Length <= 253 && Uri.CheckHostName(hostname) == UriHostNameType.Dns);
    }

    private static bool TryWebhook(string? value, bool requireHttps, out string? webhook)
    {
        webhook = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return webhook == null || (Uri.TryCreate(webhook, UriKind.Absolute, out var uri) &&
            uri.Scheme == (requireHttps ? "https" : uri.Scheme) && uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo));
    }

    private ActionResult Validation(string key, string message) => ValidationProblem(new ValidationProblemDetails(
        new Dictionary<string, string[]> { [key] = new[] { message } }));
    private MeterSettingsDto ToDto(MeterProjectSettings x) => new(x.Enabled, x.OriginBaseUrl, x.Environment,
        x.PublicGatewayHostname, x.OriginTimeoutSeconds, x.MonthlyFreeRequestAllowance, x.DefaultPriceSats,
        x.UnmatchedRouteBehavior, x.ReceiptSigningEnabled, x.WebhookUrl, x.AllowPrivateOriginInTest,
        x.MaximumRequestBodyBytes, x.MaximumResponseBodyBytes, x.LightningConnection == null ? null : ToDto(x.LightningConnection));
    private static MeterRouteRuleDto ToDto(MeterRouteRule x) => new(x.Id, x.HttpMethod, x.PathPattern, x.PriceSats,
        x.FreeRequestAllowance, x.Enabled, x.Priority, x.CredentialLifetimeSeconds, x.MaximumCredentialUses,
        x.BindRequestBody, x.CreatedAt, x.UpdatedAt);
    private static MeterLightningConnectionDto ToDto(MerchantLightningConnection x) => new(x.Id, x.ProviderType,
        x.DisplayName, x.RestUrl, !string.IsNullOrWhiteSpace(x.EncryptedTlsCertificate),
        !string.IsNullOrWhiteSpace(x.EncryptedMacaroon), x.SupportsPaymentLookup, x.LastValidatedAt);
    private MeterReceiptDto ToDto(MeterReceipt x) => new(x.Id, x.ChallengeId, x.RequestCorrelationId, x.Version,
        x.CanonicalPayload, x.Signature, x.SignatureAlgorithm, x.KeyId, _receipts.Verify(x), x.CreatedAt);
}
