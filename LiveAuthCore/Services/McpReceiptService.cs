using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LiveAuthCore.Data.Entities.Mcp;
using LiveAuthCore.Models.Mcp;

namespace LiveAuthCore.Services;

public class McpReceiptService
{
    private const string ReceiptVersion = "mcp-call-receipt-v1";
    private const string SignatureAlgorithm = "HMAC-SHA256";
    private readonly IConfiguration _configuration;

    public McpReceiptService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public McpSignedReceipt CreateReceipt(McpToolRevenueEvent revenueEvent, McpTool tool)
    {
        var body = new McpCallReceipt(
            ReceiptId: $"mcp_receipt_{revenueEvent.Id:N}",
            RevenueEventId: revenueEvent.Id,
            McpToolId: revenueEvent.McpToolId,
            ToolName: tool.Name,
            ToolSlug: tool.Slug,
            ToolMethodName: revenueEvent.ToolMethodName,
            McpGateTokenId: revenueEvent.McpGateTokenId,
            McpGateSessionId: revenueEvent.McpGateSessionId,
            PayingProjectId: revenueEvent.PayingProjectId,
            AgentId: revenueEvent.AgentId,
            GrossSats: revenueEvent.GrossSats,
            PlatformFeeSats: revenueEvent.PlatformFeeSats,
            NetSats: revenueEvent.NetSats,
            FeeBasisPoints: revenueEvent.FeeBasisPoints,
            Status: revenueEvent.Status,
            IdempotencyKey: revenueEvent.IdempotencyKey,
            RequestId: revenueEvent.RequestId,
            CreatedAt: DateTime.SpecifyKind(revenueEvent.CreatedAt, DateTimeKind.Utc)
        );

        var payload = CreatePayload(body);
        var signature = SignPayload(payload);

        return new McpSignedReceipt(
            Version: ReceiptVersion,
            Payload: payload,
            Signature: signature,
            SignatureAlgorithm: SignatureAlgorithm,
            KeyId: GetKeyId(),
            Body: body
        );
    }

    private string CreatePayload(McpCallReceipt body)
    {
        var payload = new SortedDictionary<string, object?>
        {
            ["agentId"] = body.AgentId,
            ["createdAt"] = body.CreatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["feeBasisPoints"] = body.FeeBasisPoints,
            ["grossSats"] = body.GrossSats,
            ["idempotencyKey"] = body.IdempotencyKey,
            ["mcpGateSessionId"] = body.McpGateSessionId?.ToString("D"),
            ["mcpGateTokenId"] = body.McpGateTokenId?.ToString("D"),
            ["mcpToolId"] = body.McpToolId.ToString("D"),
            ["netSats"] = body.NetSats,
            ["payingProjectId"] = body.PayingProjectId?.ToString("D"),
            ["platformFeeSats"] = body.PlatformFeeSats,
            ["receiptId"] = body.ReceiptId,
            ["requestId"] = body.RequestId,
            ["revenueEventId"] = body.RevenueEventId.ToString("D"),
            ["status"] = body.Status,
            ["toolName"] = body.ToolName,
            ["toolMethodName"] = body.ToolMethodName,
            ["toolSlug"] = body.ToolSlug,
            ["version"] = ReceiptVersion
        };

        var json = JsonSerializer.Serialize(payload);
        return Base64UrlEncode(Encoding.UTF8.GetBytes(json));
    }

    private string SignPayload(string payload)
    {
        using var hmac = new HMACSHA256(GetSigningKey());
        return Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }

    private byte[] GetSigningKey()
    {
        var secret =
            _configuration["Mcp:ReceiptSigningKey"] ??
            _configuration["Jwt:SigningKey"] ??
            _configuration["Jwt:Key"] ??
            _configuration["LiveAuth:PowHmacSecret"];

        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("No signing key configured for MCP receipts.");

        return Encoding.UTF8.GetBytes(secret);
    }

    private string GetKeyId()
        => _configuration["Mcp:ReceiptKeyId"] ?? "liveauth-mcp-receipt-v1";

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
