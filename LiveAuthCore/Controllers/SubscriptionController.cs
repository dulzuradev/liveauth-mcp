using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Models;
using LiveAuthCore.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Controllers
{
    [ApiController]
    [Route("api/dev/billing")]
    public class BillingController : ControllerBase
    {
        private readonly LiveAuthDbContext _db;
        private readonly LightningService _lightning;

        public BillingController(
            LiveAuthDbContext db,
            LightningService lightning)
        {
            _db = db;
            _lightning = lightning;
        }

        [AllowAnonymous]
        [HttpPost("subscribe")]
        public async Task<ActionResult<CreateSubscriptionInvoiceResponse>> Subscribe(
            [FromBody] CreateSubscriptionInvoiceRequest request,
            CancellationToken ct)
        {
            var project = await _db.Projects
                .SingleOrDefaultAsync(p => p.Id == request.ProjectId, ct);

            if (project == null)
                return NotFound("Project not found.");

            if (project.Plan == "pro" && project.ProPaidUntil > DateTime.UtcNow)
            {
                return BadRequest("Project already has an active Pro subscription.");
            }

            var now = DateTime.UtcNow;

            // Reuse active pending invoice (idempotent subscribe)
            var existing = await _db.BillingSubscriptions
                .Where(x => x.ProjectId == project.Id
                            && x.Plan == request.Plan
                            && !x.IsPaid
                            && x.ExpiresAt > now)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (existing != null)
            {
                return Ok(new CreateSubscriptionInvoiceResponse
                {
                    SessionId = existing.Id,
                    Invoice = existing.InvoiceBolt11,
                    AmountSats = existing.AmountSats,
                    ExpiresAtUnix = new DateTimeOffset(existing.ExpiresAt).ToUnixTimeSeconds()
                });
            }

            // v1 pricing (simple + explicit)
            var amountSats = request.Plan switch
            {
                "pro" => 10_000, // ~ $500 at current rates
                _ => throw new ArgumentException("Unknown plan")
            };

            var expiresAt = DateTime.UtcNow.AddMinutes(15);

            var memo = $"LiveAuth Pro – project {project.Name}";
            // CENTRALIZED: Use new method that returns hex payment hash
            var invoiceResult = await _lightning.CreateInvoiceWithHashAsync(
                project.Id.ToString(),
                amountSats,
                memo
            );

            var session = new BillingSubscription
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Plan = request.Plan,
                AmountSats = amountSats,
                InvoiceBolt11 = invoiceResult.Bolt11,
                InvoiceRHash = invoiceResult.PaymentHash,  // Now stores hex!
                IsPaid = false,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = expiresAt
            };

            _db.BillingSubscriptions.Add(session);
            await _db.SaveChangesAsync(ct);

            return Ok(new CreateSubscriptionInvoiceResponse
            {
                SessionId = session.Id,
                Invoice = session.InvoiceBolt11,
                AmountSats = amountSats,
                ExpiresAtUnix = new DateTimeOffset(expiresAt).ToUnixTimeSeconds()
            });
        }

        [AllowAnonymous]
        [HttpPost("confirm")]
        public async Task<ActionResult<ConfirmSubscriptionResponse>> Confirm(
            [FromBody] ConfirmSubscriptionRequest request,
            CancellationToken ct)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable, ct);
            
            var session = await _db.BillingSubscriptions
                .Include(x => x.Project)
                .SingleOrDefaultAsync(x => x.Id == request.SessionId, ct);

            if (session == null)
                return Ok(new ConfirmSubscriptionResponse { Paid = false });

            if (session.IsPaid)
            {
                return Ok(new ConfirmSubscriptionResponse
                {
                    Paid = true,
                    ProPaidUntil = session.Project.ProPaidUntil
                });
            }

            if (DateTime.UtcNow > session.ExpiresAt)
                return Ok(new ConfirmSubscriptionResponse { Paid = false });

            // Check Lightning
            var status = await _lightning.GetInvoiceStatusAsync(session.InvoiceRHash);
            if (!status.IsPaid)
            {
                await tx.CommitAsync(ct);
                return Ok(new ConfirmSubscriptionResponse { Paid = false });
            }

            // Apply upgrade once
            session.IsPaid = true;
            session.PaidAt = DateTime.UtcNow;

            var now = DateTime.UtcNow;
            var baseDate = session.Project.ProPaidUntil.HasValue && session.Project.ProPaidUntil.Value > now
                ? session.Project.ProPaidUntil.Value
                : now;

            session.Project.Plan = "pro";
            session.Project.ProPaidUntil = baseDate.AddDays(30);

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return Ok(new ConfirmSubscriptionResponse
            {
                Paid = true,
                ProPaidUntil = session.Project.ProPaidUntil
            });
        }
    }
}