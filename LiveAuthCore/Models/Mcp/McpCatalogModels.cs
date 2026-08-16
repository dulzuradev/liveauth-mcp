using System.Text.Json.Serialization;

namespace LiveAuthCore.Models.Mcp;

/// <summary>
/// Public-facing DTO returned by <c>GET /api/mcp/tools</c>. Slim shape so we
/// don't leak internal fields (DeveloperId, WebhookUrl, timestamps) to anyone
/// holding a project key.
/// </summary>
public sealed record McpCatalogToolDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("category")] string? Category,
    [property: JsonPropertyName("defaultCostSats")] int DefaultCostSats,
    [property: JsonPropertyName("minCostSats")] int MinCostSats,
    [property: JsonPropertyName("maxCostSats")] int MaxCostSats,
    [property: JsonPropertyName("visibility")] string Visibility);

/// <summary>
/// Response envelope for the catalog endpoint.
/// </summary>
public sealed record McpCatalogResponse(
    [property: JsonPropertyName("tools")] IReadOnlyList<McpCatalogToolDto> Tools,
    [property: JsonPropertyName("count")] int Count);