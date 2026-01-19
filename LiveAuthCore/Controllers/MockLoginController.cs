using LiveAuthCore.Entities;
using LiveAuthCore.Services;
using Microsoft.AspNetCore.Mvc;

namespace LiveAuthCore.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MockLoginController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly LightningService _lightningService;

        public MockLoginController(AppDbContext dbContext, LightningService lightningService)
        {
            _dbContext = dbContext;
            _lightningService = lightningService;
        }

        [HttpPost]
        public async Task<IActionResult> MockLogin([FromBody] MockLoginRequest request)
        {
            // Mock validation: success if username == password
            bool isSuccessful = request.Username == request.Password;

            // Log the attempt
            var attempt = new LoginAttempt
            {
                UserId = request.Username, // Mock user ID
                PaymentHash = request.PaymentHash,
                IsSuccessful = isSuccessful,
                AttemptTime = DateTime.UtcNow,
                IsRefunded = false,
                RefundPaymentHash = request.PaymentHash
            };
            _dbContext.LoginAttempts.Add(attempt);
            await _dbContext.SaveChangesAsync();

            if (isSuccessful)
            {
                // Initiate refund (100 satoshis)
                try
                {
                    // Assume user provides refund invoice (mocked for demo)
                    string refundInvoice = await GetRefundInvoice(request.Username); // Implement this
                    var (refundPaymentHash, _) = await _lightningService.PayInvoice(refundInvoice);
                    attempt.IsRefunded = true;
                    attempt.RefundPaymentHash = refundPaymentHash;
                    await _dbContext.SaveChangesAsync();
                    return Ok(new { message = "Login successful, satoshis refunded!", isSuccessful = true });
                }
                catch (Exception ex)
                {
                    return Ok(new { message = "Login successful, but refund failed: " + ex.Message, isSuccessful = true });
                }
            }
            else
            {
                // Sats forfeited (no action needed, kept by node)
                var token = HttpContext.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
                _dbContext.RevokedTokens.Add(new RevokedToken { Token = token, RevokedAt = DateTime.UtcNow });
                await _dbContext.SaveChangesAsync();
                return BadRequest(new { message = "Login failed, satoshis forfeited.", isSuccessful = false });
            }
        }

        // Mock method to get user's refund invoice
        private Task<string> GetRefundInvoice(string userId)
        {
            // In a real system, user provides their Lightning invoice for refund
            // For demo, return a placeholder or integrate with wallet API
            return Task.FromResult("lnbcrt1u1p...");
        }
    }

    public class MockLoginRequest
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public string PaymentHash { get; set; }
    }
}
