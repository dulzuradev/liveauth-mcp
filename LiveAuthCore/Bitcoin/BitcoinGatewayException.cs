namespace LiveAuthCore.Bitcoin;

public static class BitcoinErrorCodes
{
    public const string InvalidTransaction = "LIVEAUTH_BITCOIN_INVALID_TX";
    public const string TransactionRejected = "LIVEAUTH_BITCOIN_TX_REJECTED";
    public const string FeeLimitExceeded = "LIVEAUTH_BITCOIN_FEE_LIMIT_EXCEEDED";
    public const string MempoolConflict = "LIVEAUTH_BITCOIN_MEMPOOL_CONFLICT";
    public const string MissingInput = "LIVEAUTH_BITCOIN_MISSING_INPUT";
    public const string AlreadyKnown = "LIVEAUTH_BITCOIN_ALREADY_KNOWN";
    public const string NodeUnavailable = "LIVEAUTH_BITCOIN_NODE_UNAVAILABLE";
    public const string RpcTimeout = "LIVEAUTH_BITCOIN_RPC_TIMEOUT";
    public const string RateLimited = "LIVEAUTH_BITCOIN_RATE_LIMITED";
    public const string IdempotencyConflict = "LIVEAUTH_BITCOIN_IDEMPOTENCY_CONFLICT";
    public const string OperationInProgress = "LIVEAUTH_BITCOIN_OPERATION_IN_PROGRESS";
    public const string PaymentDenied = "LIVEAUTH_BITCOIN_PAYMENT_DENIED";
    public const string Disabled = "LIVEAUTH_BITCOIN_GATEWAY_DISABLED";
}

public sealed class BitcoinGatewayException : Exception
{
    public BitcoinGatewayException(
        string code,
        string message,
        bool retryable = false,
        int statusCode = StatusCodes.Status400BadRequest,
        int? retryAfterSeconds = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        Retryable = retryable;
        StatusCode = statusCode;
        RetryAfterSeconds = retryAfterSeconds;
    }

    public string Code { get; }
    public bool Retryable { get; }
    public int StatusCode { get; }
    public int? RetryAfterSeconds { get; }
}

public sealed class BitcoinNodeRpcException : Exception
{
    public BitcoinNodeRpcException(int rpcCode, string rpcMessage)
        : base(rpcMessage)
    {
        RpcCode = rpcCode;
        RpcMessage = rpcMessage;
    }

    public int RpcCode { get; }
    public string RpcMessage { get; }
}
