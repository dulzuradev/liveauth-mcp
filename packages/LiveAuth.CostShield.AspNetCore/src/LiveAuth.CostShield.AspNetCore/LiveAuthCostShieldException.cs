using System.Net;

namespace LiveAuth.CostShield.AspNetCore;

/// <summary>A structured CostShield validation or API failure.</summary>
public sealed class LiveAuthCostShieldException : Exception
{
    /// <summary>Creates a structured CostShield exception.</summary>
    public LiveAuthCostShieldException(
        string code,
        string message,
        HttpStatusCode? statusCode = null,
        bool retryable = false,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        StatusCode = statusCode;
        Retryable = retryable;
    }

    /// <summary>The stable machine-readable error code.</summary>
    public string Code { get; }

    /// <summary>The related upstream or recommended HTTP status.</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>Whether retrying the operation may succeed.</summary>
    public bool Retryable { get; }
}
