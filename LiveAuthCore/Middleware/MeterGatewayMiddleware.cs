using System.Diagnostics;
using System.Security.Cryptography;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Services;
using LiveAuthCore.Services.CostShield;
using LiveAuthCore.Services.Meter;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LiveAuthCore.Middleware;

public sealed class MeterGatewayMiddleware
{
    private readonly RequestDelegate _next;
    public MeterGatewayMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var db = context.RequestServices.GetRequiredService<LiveAuthDbContext>();
        var resolved = await ResolveAsync(context, db);
        if (resolved == null)
        {
            await _next(context);
            return;
        }

        var (settings, path) = resolved.Value;
        var correlationId = "meter_" + Guid.NewGuid().ToString("N");
        context.Response.Headers["X-LiveAuth-Request-Id"] = correlationId;
        var clock = Stopwatch.StartNew();
        try
        {
            if (!settings.Enabled || !settings.Project.IsActive || settings.Project.IsDeleted)
            {
                await Error(context, 404, "meter_not_found", "Meter gateway is not available.", correlationId);
                return;
            }

            var matcher = context.RequestServices.GetRequiredService<IMeterRouteMatcher>();
            var normalizedPath = MeterRouteMatcher.NormalizePath(path);
            var rules = await db.MeterRouteRules.AsNoTracking().Where(x => x.ProjectId == settings.ProjectId && x.Enabled).ToListAsync(context.RequestAborted);
            var route = matcher.Match(settings, rules, context.Request.Method, normalizedPath);
            var hasher = context.RequestServices.GetRequiredService<IClientContextHasher>();
            var callerKey = hasher.HashContext(settings.ProjectId,
                context.Connection.RemoteIpAddress?.ToString(), context.Request.Headers.UserAgent.ToString(), null);
            if (route.IsBlocked)
            {
                await RecordAndWebhook(context, settings, route, callerKey, correlationId, "DENIED", 0, null, "route_blocked", clock, db);
                await Error(context, 403, "route_blocked", "This route is not enabled by the Meter policy.", correlationId);
                return;
            }

            var body = await ReadBodyAsync(context, settings.MaximumRequestBodyBytes);
            var bodyHash = body.Length == 0 ? null : Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
            var allowance = context.RequestServices.GetRequiredService<IMeterAllowanceService>();
            var isFree = route.IsFree || await allowance.TryConsumeFreeRequestAsync(settings.ProjectId, settings.Environment,
                callerKey, route.Rule?.Id, route.Rule?.FreeRequestAllowance ?? 0,
                settings.MonthlyFreeRequestAllowance, context.RequestAborted);

            MeterPaymentChallenge? paidChallenge = null;
            var newlySettled = false;
            var authorizationHeader = context.Request.Headers.Authorization.ToString();
            if (!isFree && authorizationHeader.StartsWith("L402 ", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryTakePermit(context, $"meter:verify-rate:{settings.ProjectId:N}:{callerKey}", 60))
                {
                    await Error(context, 429, "payment_verification_rate_limited",
                        "Too many payment verification attempts were requested.", correlationId);
                    return;
                }
                var payments = context.RequestServices.GetRequiredService<IMeterPaymentService>();
                var authorization = await payments.AuthorizeAsync(settings, route, context.Request.Method,
                    normalizedPath, bodyHash, authorizationHeader, context.RequestAborted);
                if (!authorization.Authorized)
                {
                    var status = authorization.Error == "payment_not_settled" ? 402 : 401;
                    await RecordAndWebhook(context, settings, route, callerKey, correlationId, "DENIED", route.PriceSats,
                        authorization.Challenge?.Id, authorization.Error, clock, db);
                    await Error(context, status, authorization.Error ?? "authorization_failed",
                        "The L402 credential was not accepted.", correlationId);
                    return;
                }
                paidChallenge = authorization.Challenge;
                newlySettled = authorization.NewlySettled;
            }
            else if (!isFree)
            {
                if (!TryTakeChallengePermit(context, settings.ProjectId, callerKey))
                {
                    await Error(context, 429, "challenge_rate_limited", "Too many payment challenges were requested.", correlationId);
                    return;
                }
                var payments = context.RequestServices.GetRequiredService<IMeterPaymentService>();
                var challenge = await payments.CreateOrReuseChallengeAsync(settings.Project, settings, route,
                    context.Request.Method, normalizedPath, callerKey, correlationId, bodyHash, context.RequestAborted);
                db.MeterUsageEvents.Add(NewEvent(settings, route, callerKey, correlationId, "CHALLENGE", route.PriceSats,
                    challenge.Challenge.Id, null, null, clock.ElapsedMilliseconds));
                await db.SaveChangesAsync(context.RequestAborted);
                context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
                context.Response.Headers.WWWAuthenticate = $"L402 macaroon=\"{challenge.Challenge.Macaroon}\", invoice=\"{challenge.Challenge.Invoice}\"";
                context.Response.Headers["X-LiveAuth-Price-Sats"] = route.PriceSats.ToString();
                context.Response.Headers["X-LiveAuth-Challenge-Id"] = challenge.Challenge.Id.ToString("D");
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "payment_required", challengeId = challenge.Challenge.Id,
                    amountSats = route.PriceSats, expiresAt = challenge.Challenge.ExpiresAt,
                    requestId = correlationId
                }, context.RequestAborted);
                return;
            }

            var proxy = context.RequestServices.GetRequiredService<IMeterOriginProxy>();
            var requestAt = DateTime.UtcNow;
            await proxy.ForwardAsync(context, settings, normalizedPath, body, clock,
                async (originStatus, originLatency, gatewayLatency) =>
                {
                    var metadata = new Dictionary<string, string>();
                    var kind = paidChallenge != null ? "PAID" : "FREE";
                    db.MeterUsageEvents.Add(NewEvent(settings, route, callerKey, correlationId, kind,
                        paidChallenge?.PriceSats ?? 0, paidChallenge?.Id, originStatus, null, gatewayLatency, originLatency));
                    if (paidChallenge != null && settings.ReceiptSigningEnabled)
                    {
                        var receipts = context.RequestServices.GetRequiredService<IMeterReceiptService>();
                        var receipt = receipts.Create(new MeterReceiptInput(settings.ProjectId, settings.Project.DeveloperId,
                            settings.Environment, context.Request.Method.ToUpperInvariant(), route.NormalizedRoute,
                            requestAt, DateTime.UtcNow, paidChallenge.PriceSats, paidChallenge.PaymentHash,
                            paidChallenge.Id, correlationId, originStatus, gatewayLatency, originLatency));
                        db.MeterReceipts.Add(receipt);
                        metadata["X-LiveAuth-Receipt-Id"] = receipt.Id.ToString("D");
                        metadata["X-LiveAuth-Receipt-Signature"] = receipt.Signature;
                    }
                    await db.SaveChangesAsync(context.RequestAborted);
                    var webhooks = context.RequestServices.GetRequiredService<WebhookService>();
                    var authorizedEventId = Guid.NewGuid();
                    await webhooks.EnqueueWithIdAsync(settings.Project, "meter.request.authorized", new
                    {
                        eventId = authorizedEventId, projectId = settings.ProjectId, challengeId = paidChallenge?.Id,
                        requestId = correlationId, method = context.Request.Method, route = route.NormalizedRoute,
                        amountSats = paidChallenge?.PriceSats ?? 0, originStatusCode = originStatus, createdAt = DateTime.UtcNow
                    }, settings.WebhookUrl, authorizedEventId, context.RequestAborted);
                    if (paidChallenge != null && newlySettled)
                    {
                        var paymentEventId = Guid.NewGuid();
                        await webhooks.EnqueueWithIdAsync(settings.Project, "meter.payment.completed", new
                        {
                            eventId = paymentEventId, projectId = settings.ProjectId, challengeId = paidChallenge.Id,
                            paymentHash = paidChallenge.PaymentHash, amountSats = paidChallenge.PriceSats,
                            paidAt = paidChallenge.PaidAt
                        }, settings.WebhookUrl, paymentEventId, context.RequestAborted);
                    }
                    return metadata;
                }, context.RequestAborted);
        }
        catch (MeterProxyException ex)
        {
            if (!context.Response.HasStarted)
            {
                await RecordOriginError(context, settings, correlationId, path, ex.Code, clock, db);
                await Error(context, ex.StatusCode, ex.Code, ex.Message, correlationId);
            }
        }
        catch (MeterSecurityException ex)
        {
            await Error(context, 502, ex.Code, "The configured origin is not safe to proxy.", correlationId);
        }
        catch (MeterConfigurationException ex)
        {
            await Error(context, 503, ex.Code, ex.Message, correlationId);
        }
    }

    private static async Task<(MeterProjectSettings Settings, string Path)?> ResolveAsync(HttpContext context, LiveAuthDbContext db)
    {
        var path = context.Request.Path.Value ?? "/";
        if (path.StartsWith("/gateway/", StringComparison.OrdinalIgnoreCase))
        {
            var parts = path.Split('/', 4, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return null;
            var identifier = parts[1];
            var settings = await db.MeterProjectSettings.Include(x => x.Project).Include(x => x.LightningConnection)
                .SingleOrDefaultAsync(x => x.Project.PublicKey == identifier || x.ProjectId.ToString() == identifier, context.RequestAborted);
            return settings == null ? null : (settings, parts.Length >= 3 ? "/" + parts[2] + (parts.Length == 4 ? "/" + parts[3] : "") : "/");
        }
        var host = context.Request.Host.Host.TrimEnd('.').ToLowerInvariant();
        var byHost = await db.MeterProjectSettings.Include(x => x.Project).Include(x => x.LightningConnection)
            .SingleOrDefaultAsync(x => x.PublicGatewayHostname == host, context.RequestAborted);
        return byHost == null ? null : (byHost, path);
    }

    private static async Task<byte[]> ReadBodyAsync(HttpContext context, long maximumBytes)
    {
        if (context.Request.ContentLength > maximumBytes)
            throw new MeterProxyException("request_body_too_large", 413, "The request body exceeds the configured limit.");
        using var memory = new MemoryStream();
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var read = await context.Request.Body.ReadAsync(buffer, context.RequestAborted);
            if (read == 0) break;
            total += read;
            if (total > maximumBytes) throw new MeterProxyException("request_body_too_large", 413, "The request body exceeds the configured limit.");
            await memory.WriteAsync(buffer.AsMemory(0, read), context.RequestAborted);
        }
        return memory.ToArray();
    }

    private static bool TryTakeChallengePermit(HttpContext context, Guid projectId, string callerKey)
        => TryTakePermit(context, $"meter:challenge-rate:{projectId:N}:{callerKey}", 10);

    private static bool TryTakePermit(HttpContext context, string key, int limit)
    {
        var cache = context.RequestServices.GetRequiredService<IMemoryCache>();
        var gate = cache.GetOrCreate(key, entry => { entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1); return new ChallengeRateState(); })!;
        lock (gate) { if (gate.Count >= limit) return false; gate.Count++; return true; }
    }

    private static MeterUsageEvent NewEvent(MeterProjectSettings settings, MeterRouteDecision route, string callerKey,
        string correlationId, string kind, long amount, Guid? challengeId, int? originStatus, string? error,
        long gatewayLatency, long? originLatency = null) => new()
    {
        ProjectId = settings.ProjectId, RouteRuleId = route.Rule?.Id, ChallengeId = challengeId,
        Environment = settings.Environment, Kind = kind, HttpMethod = route.Rule?.HttpMethod ?? "*",
        Path = route.NormalizedRoute, NormalizedRoute = route.NormalizedRoute, AmountSats = amount,
        OriginStatusCode = originStatus, GatewayLatencyMilliseconds = gatewayLatency,
        OriginLatencyMilliseconds = originLatency, CorrelationId = correlationId, CallerKey = callerKey,
        ErrorCode = error, CreatedAt = DateTime.UtcNow
    };

    private static async Task RecordAndWebhook(HttpContext context, MeterProjectSettings settings,
        MeterRouteDecision route, string callerKey, string correlationId, string kind, long amount,
        Guid? challengeId, string? error, Stopwatch clock, LiveAuthDbContext db)
    {
        db.MeterUsageEvents.Add(NewEvent(settings, route, callerKey, correlationId, kind, amount, challengeId, null, error, clock.ElapsedMilliseconds));
        await db.SaveChangesAsync(context.RequestAborted);
        var webhooks = context.RequestServices.GetRequiredService<WebhookService>();
        var eventId = Guid.NewGuid();
        await webhooks.EnqueueWithIdAsync(settings.Project, "meter.request.denied", new
        {
            eventId, projectId = settings.ProjectId, requestId = correlationId,
            method = context.Request.Method, route = route.NormalizedRoute, reason = error, createdAt = DateTime.UtcNow
        }, settings.WebhookUrl, eventId, context.RequestAborted);
    }

    private static async Task RecordOriginError(HttpContext context, MeterProjectSettings settings,
        string correlationId, string path, string error, Stopwatch clock, LiveAuthDbContext db)
    {
        db.MeterUsageEvents.Add(new MeterUsageEvent { ProjectId = settings.ProjectId, Environment = settings.Environment,
            Kind = "ORIGIN_ERROR", HttpMethod = context.Request.Method, Path = path, NormalizedRoute = path,
            CorrelationId = correlationId, CallerKey = "redacted", ErrorCode = error,
            GatewayLatencyMilliseconds = clock.ElapsedMilliseconds });
        await db.SaveChangesAsync(context.RequestAborted);
        var webhooks = context.RequestServices.GetRequiredService<WebhookService>();
        var eventId = Guid.NewGuid();
        await webhooks.EnqueueWithIdAsync(settings.Project, "meter.origin.error", new
        {
            eventId, projectId = settings.ProjectId, requestId = correlationId,
            method = context.Request.Method, path, error, createdAt = DateTime.UtcNow
        }, settings.WebhookUrl, eventId, context.RequestAborted);
    }

    private static async Task Error(HttpContext context, int status, string code, string message, string requestId)
    {
        if (context.Response.HasStarted) return;
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new { error = code, message, requestId }, context.RequestAborted);
    }

    private sealed class ChallengeRateState { public int Count { get; set; } }
}
