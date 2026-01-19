using LiveAuthCore.Entities;

namespace LiveAuthCore.Middleware
{
    public class TokenValidationMiddleware
    {
        private readonly RequestDelegate _next;

        public TokenValidationMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, AppDbContext dbContext)
        {
            var token = context.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
            if (!string.IsNullOrEmpty(token) && dbContext.RevokedTokens.Any(t => t.Token == token))
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("Token has been revoked.");
                return;
            }
            await _next(context);
        }
    }
}
