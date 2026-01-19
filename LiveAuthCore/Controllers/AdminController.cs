using LiveAuthCore.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Controllers
{
    /// <summary>
    /// 
    /// </summary>
    [Authorize(Roles = "Admin")]
    [ApiController]
    [Route("api/[controller]")]
    public class AdminController : ControllerBase
    {
        private readonly AppDbContext _dbContext;

        public AdminController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        [HttpGet("login-attempts")]
        public async Task<IActionResult> GetLoginAttempts()
        {
            var attempts = await _dbContext.LoginAttempts
                .Select(a => new
                {
                    a.UserId,
                    a.PaymentHash,
                    a.IsSuccessful,
                    a.AttemptTime,
                    a.IsRefunded,
                    a.RefundPaymentHash
                })
                .ToListAsync();
            return Ok(attempts);
        }
    }
}
