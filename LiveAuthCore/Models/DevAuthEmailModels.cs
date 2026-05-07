namespace LiveAuthCore.Models;

// ─── Register ───────────────────────────────────────────────

public class RegisterRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class RegisterResponse
{
    public Guid DeveloperId { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool EmailVerificationRequired { get; set; }
    public bool EmailSent { get; set; }
}

// ─── Verify Email ─────────────────────────────────────────────

public class VerifyEmailRequest
{
    public string Token { get; set; } = string.Empty;
}

public class VerifyEmailResponse
{
    public bool Success { get; set; }
    public string? Token { get; set; }  // developer JWT on success
    public string Message { get; set; } = string.Empty;
}

// ─── Login ─────────────────────────────────────────────────────

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class LoginResponse
{
    public bool Verified { get; set; }
    public string? Token { get; set; }  // developer JWT on success
    public string Message { get; set; } = string.Empty;
}

// ─── Forgot Password ───────────────────────────────────────────

public class ForgotPasswordRequest
{
    public string Email { get; set; } = string.Empty;
}

public class ForgotPasswordResponse
{
    public bool EmailSent { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class ResetPasswordRequest
{
    public string Token { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class ResetPasswordResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

// ─── Resend Verification ───────────────────────────────────────

public class ResendVerificationRequest
{
    public string Email { get; set; } = string.Empty;
}