using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LiveAuthCore.Data.Entities;

namespace LiveAuthCore.Services.Meter;

public sealed record MeterReceiptInput(
    Guid ProjectId, Guid MerchantId, string Environment, string Method,
    string NormalizedRoute, DateTime RequestTimestamp, DateTime AuthorizedAt,
    long AmountSats, string PaymentHash, Guid ChallengeId, string CorrelationId,
    int OriginStatusCode, long GatewayLatencyMilliseconds, long OriginLatencyMilliseconds);

public interface IMeterReceiptService
{
    MeterReceipt Create(MeterReceiptInput input);
    bool Verify(MeterReceipt receipt);
}

public sealed class MeterReceiptService : IMeterReceiptService
{
    private const string Version = "meter-receipt-v1";
    private readonly byte[] _key;
    private readonly string _keyId;

    public MeterReceiptService(IConfiguration configuration)
    {
        var secret = configuration["Meter:ReceiptSigningKey"] ?? configuration["Mcp:ReceiptSigningKey"] ??
            configuration["Jwt:SigningKey"] ?? configuration["Jwt:Key"] ?? configuration["LiveAuth:PowHmacSecret"];
        if (string.IsNullOrWhiteSpace(secret)) throw new InvalidOperationException("Meter receipt signing key is not configured.");
        _key = Encoding.UTF8.GetBytes(secret);
        _keyId = configuration["Meter:ReceiptKeyId"] ?? "liveauth-meter-v1";
    }

    public MeterReceipt Create(MeterReceiptInput input)
    {
        var id = Guid.NewGuid();
        var body = new SortedDictionary<string, object?>
        {
            ["amountPaidSats"] = input.AmountSats,
            ["authorizationTimestamp"] = input.AuthorizedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["challengeId"] = input.ChallengeId.ToString("D"),
            ["environment"] = input.Environment,
            ["gatewayLatencyMilliseconds"] = input.GatewayLatencyMilliseconds,
            ["merchantId"] = input.MerchantId.ToString("D"),
            ["method"] = input.Method,
            ["normalizedRoute"] = input.NormalizedRoute,
            ["originLatencyMilliseconds"] = input.OriginLatencyMilliseconds,
            ["originStatusCode"] = input.OriginStatusCode,
            ["paymentHash"] = input.PaymentHash,
            ["projectId"] = input.ProjectId.ToString("D"),
            ["receiptId"] = id.ToString("D"),
            ["requestCorrelationId"] = input.CorrelationId,
            ["requestTimestamp"] = input.RequestTimestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["version"] = Version
        };
        var payload = JsonSerializer.Serialize(body);
        return new MeterReceipt
        {
            Id = id, ProjectId = input.ProjectId, ChallengeId = input.ChallengeId,
            RequestCorrelationId = input.CorrelationId, Version = Version,
            CanonicalPayload = payload, Signature = Sign(payload), KeyId = _keyId,
            CreatedAt = DateTime.UtcNow
        };
    }

    public bool Verify(MeterReceipt receipt)
    {
        byte[] actual;
        byte[] expected;
        try { actual = Convert.FromBase64String(receipt.Signature); expected = Convert.FromBase64String(Sign(receipt.CanonicalPayload)); }
        catch (FormatException) { return false; }
        return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private string Sign(string payload)
    {
        using var hmac = new HMACSHA256(_key);
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }
}
