using LiveAuthCore.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;

namespace LiveAuthCore.Controllers
{
    using LiveAuthCore.Entities;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.EntityFrameworkCore;
    using System.Threading.Tasks;
    using static LiveAuthCore.Services.LightningService;

    [ApiController]
    [Route("api/[controller]")]
    public class LoginController : ControllerBase
    {
        private readonly LightningService _lightningService;
        private readonly AppDbContext _dbContext;

        public LoginController(AppDbContext dbContext, LightningService lightningService)
        {
            _dbContext = dbContext;
            _lightningService = lightningService;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            // Mock subscription check (replace with real database logic later)
            if (request.UserId == "subscribed_user")
            {
                var token = _lightningService.GenerateJwtToken(request.UserId);
                return Ok(new { Data = "Access granted", Token = token });
            }

            var invoice = await _lightningService.CreateInvoice(request.UserId, 100, "Login microtransaction");
            
            return Ok(new { invoice });
        }

        /// <summary>
        /// Original endpoint: GET /api/Login/payment-status/{paymentHash}
        /// </summary>
        [HttpGet("payment-status/{paymentHash}")]
        public async Task<IActionResult> GetPaymentStatus(string paymentHash)
        {
            bool invoicePayed = await _lightningService.CheckPaymentStatus(paymentHash);
            if (invoicePayed)
            {
                // Mock userId retrieval (replace with database mapping)
                var userId = "user123";
                var token = _lightningService.GenerateJwtToken(userId);
                return Ok(new { Data = "Access granted", Token = token });
            }

            return Ok(new { Status = "Pending" });
        }

        /// <summary>
        /// Compatibility endpoint for demo:
        /// GET /api/Login/payment-status/{sessionId}/{paymentHash}
        /// It just ignores the sessionId and forwards the hash to the original logic.
        /// </summary>
        [HttpGet("payment-status/{sessionId}/{paymentHash}")]
        public Task<IActionResult> GetPaymentStatusWithSession(string sessionId, string paymentHash)
        {
            // sessionId is currently unused; we just delegate to the original method
            return GetPaymentStatus(paymentHash);
        }

        
    }

    /// <summary>
    /// 
    /// </summary>
    public class LoginRequest
    {
        public string UserId { get; set; }
    }
    
    public sealed class PaymentStatusResponse
    {
        public string SessionId { get; set; } = string.Empty;
        public bool IsPaid { get; set; }
    }
}
