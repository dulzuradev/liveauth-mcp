using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using LiveAuthCore.Models.PermitSignal;
using LiveAuthCore.Services.PermitSignal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace LiveAuthCore.Controllers.PermitSignal;

[ApiController]
[Route("api/permitsignal/mcp")]
[Authorize(Roles = "McpClient")]
[EnableRateLimiting("permitsignal")]
public sealed class PermitSignalMcpController : ControllerBase
{
    private const string ProtocolVersion = "2025-06-18";
    private readonly IPermitQueryService _queries;
    private readonly IPermitSignalMeteringService _meter;
    private readonly PermitSignalToolOptions _tools;
    private readonly ILogger<PermitSignalMcpController> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public PermitSignalMcpController(IPermitQueryService queries, IPermitSignalMeteringService meter,
        IOptions<PermitSignalOptions> options, ILogger<PermitSignalMcpController> logger)
    {
        _queries = queries;
        _meter = meter;
        _tools = options.Value.Tools;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Handle([FromBody] McpJsonRpcRequest request, CancellationToken ct)
    {
        if (request.Jsonrpc != "2.0") return RpcError(request.Id, -32600, "jsonrpc must be '2.0'.");
        if (request.Method == "notifications/initialized") return NoContent();

        try
        {
            return request.Method switch
            {
                "initialize" => RpcResult(request.Id, new
                {
                    protocolVersion = ProtocolVersion,
                    capabilities = new { tools = new { listChanged = false } },
                    serverInfo = new { name = "PermitSignal", version = "1.0.0" },
                    instructions = "Paid construction permit intelligence powered by LiveAuth Meter. All project records preserve official source provenance."
                }),
                "ping" => RpcResult(request.Id, new { }),
                "tools/list" => RpcResult(request.Id, new { tools = ToolDefinitions() }),
                "tools/call" => await CallToolAsync(request, ct),
                _ => RpcError(request.Id, -32601, $"Unknown method '{request.Method}'.")
            };
        }
        catch (PermitSignalValidationException ex)
        {
            return RpcError(request.Id, -32602, ex.Message);
        }
        catch (JsonException ex)
        {
            return RpcError(request.Id, -32602, $"Invalid tool arguments: {ex.Message}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PermitSignal MCP method {Method} failed before metering completed.", request.Method);
            return RpcError(request.Id, -32603, "PermitSignal could not complete the tool call. The call was not charged.");
        }
    }

    private async Task<IActionResult> CallToolAsync(McpJsonRpcRequest request, CancellationToken ct)
    {
        if (!request.Params.HasValue || request.Params.Value.ValueKind != JsonValueKind.Object)
            return RpcError(request.Id, -32602, "tools/call requires params.");
        var parameters = request.Params.Value;
        if (!parameters.TryGetProperty("name", out var nameElement) || string.IsNullOrWhiteSpace(nameElement.GetString()))
            return RpcError(request.Id, -32602, "Tool name is required.");
        var name = nameElement.GetString()!;
        var arguments = parameters.TryGetProperty("arguments", out var argumentElement) && argumentElement.ValueKind == JsonValueKind.Object
            ? argumentElement
            : EmptyObject();
        var idempotencyKey = Request.Headers["X-LiveAuth-Idempotency-Key"].FirstOrDefault();
        var started = DateTime.UtcNow;
        object result;
        string slug;
        int price;

        switch (name)
        {
            case "search_projects":
            {
                var input = DeserializeAndValidate<SearchProjectsRequest>(arguments);
                result = await _queries.SearchAsync(input, ct);
                slug = "permitsignal-search-projects";
                price = _tools.SearchProjects.PriceSats;
                break;
            }
            case "find_opportunities":
            {
                var input = DeserializeAndValidate<FindOpportunitiesRequest>(arguments);
                result = await _queries.FindOpportunitiesAsync(input, ct);
                slug = "permitsignal-find-opportunities";
                price = _tools.FindOpportunities.PriceSats;
                break;
            }
            case "analyze_project":
            {
                var input = DeserializeAndValidate<AnalyzeProjectRequest>(arguments);
                result = await _queries.AnalyzeProjectAsync(input, ct) is { } analysis
                    ? new { found = true, analysis }
                    : new { found = false, projectId = input.ProjectId };
                slug = "permitsignal-analyze-project";
                price = _tools.AnalyzeProject.PriceSats;
                break;
            }
            case "property_history":
            {
                var input = DeserializeAndValidate<PropertyHistoryRequest>(arguments);
                result = await _queries.PropertyHistoryAsync(input, ct);
                slug = "permitsignal-property-history";
                price = _tools.PropertyHistory.PriceSats;
                break;
            }
            default:
                return RpcError(request.Id, -32602, $"Unknown PermitSignal tool '{name}'.");
        }

        var resultCount = result switch
        {
            SearchProjectsResponse search => search.Count,
            FindOpportunitiesResponse opportunities => opportunities.Count,
            PropertyHistoryResponse history => history.TotalPermits,
            _ => 1
        };
        var metering = await _meter.ChargeSuccessfulCallAsync(User, slug, price, idempotencyKey,
            HttpContext.TraceIdentifier, new Dictionary<string, object?>
            {
                ["product"] = "PermitSignal", ["tool"] = name, ["resultCount"] = resultCount,
                ["durationMilliseconds"] = (long)(DateTime.UtcNow - started).TotalMilliseconds
            }, ct);
        if (!metering.Authorized)
            return RpcError(request.Id, -32002, $"LiveAuth Meter denied the paid call: {metering.Reason}.", new
            {
                reason = metering.Reason, priceSats = metering.PriceSats,
                callsUsed = metering.CallsUsed, satsUsed = metering.SatsUsed
            });

        var serialized = JsonSerializer.Serialize(result, JsonOptions);
        return RpcResult(request.Id, new Dictionary<string, object?>
        {
            ["content"] = new[] { new { type = "text", text = serialized } },
            ["structuredContent"] = result,
            ["isError"] = false,
            ["_meta"] = new
            {
                liveauth = new
                {
                    paid = true, priceSats = metering.PriceSats, revenueEventId = metering.RevenueEventId,
                    receipt = metering.Receipt, callsUsed = metering.CallsUsed, satsUsed = metering.SatsUsed
                }
            }
        });
    }

    private static T DeserializeAndValidate<T>(JsonElement arguments) where T : new()
    {
        var model = arguments.Deserialize<T>(JsonOptions) ?? new T();
        var validation = new List<ValidationResult>();
        if (!Validator.TryValidateObject(model, new ValidationContext(model), validation, true))
            throw new PermitSignalValidationException(string.Join("; ", validation.Select(item => item.ErrorMessage)));
        return model;
    }

    private static object[] ToolDefinitions() =>
    [
        new
        {
            name = "search_projects",
            description = "Paid: search normalized public construction permits across supported cities. Use date, value, permit type, work category, occupancy, keywords, contractor, and location filters. Returns structured projects with official source provenance.",
            inputSchema = new { type = "object", properties = SearchProperties(), additionalProperties = false }
        },
        new
        {
            name = "find_opportunities",
            description = "Paid: find recently issued construction projects representing explainable trade-specific sales opportunities. Use for HVAC, electrical, plumbing, roofing, solar, fire protection, mechanical, structural, demolition, or general construction leads.",
            inputSchema = new { type = "object", properties = OpportunityProperties(), required = new[] { "trade" }, additionalProperties = false }
        },
        new
        {
            name = "analyze_project",
            description = "Paid: analyze a permit by PermitSignal project ID, official source record ID, or permit number. Returns scope, stage, likely trades, supplier/service opportunities, signals, and source records.",
            inputSchema = new { type = "object", properties = new { project_id = StringSchema("PermitSignal ID, source record ID, or permit number") }, required = new[] { "project_id" }, additionalProperties = false }
        },
        new
        {
            name = "property_history",
            description = "Paid: retrieve permits for one exact-normalized property address, newest first, with summary statistics, common categories, major projects, and provenance. PermitSignal will not guess across low-confidence addresses.",
            inputSchema = new { type = "object", properties = new { address = StringSchema("Complete street address"), municipality = StringSchema("Optional city"), state = StringSchema("Optional two-letter state"), limit = IntegerSchema(1, 100, 50) }, required = new[] { "address" }, additionalProperties = false }
        }
    ];

    private static object SearchProperties() => new
    {
        location = StringSchema("City or City, ST"), municipality = StringSchema("Municipality"), state = StringSchema("Two-letter state"),
        issued_after = DateSchema(), issued_before = DateSchema(), minimum_project_value = NumberSchema(), maximum_project_value = NumberSchema(),
        permit_type = StringSchema("Permit type contains"), work_category = new { type = "string", @enum = WorkCategoryValues() },
        commercial_only = new { type = "boolean", @default = false }, residential_only = new { type = "boolean", @default = false },
        keywords = StringSchema("Up to five space-separated scope keywords"), contractor_name = StringSchema("Contractor name contains"), limit = IntegerSchema(1, 100, 25)
    };

    private static object OpportunityProperties() => new
    {
        location = StringSchema("City or City, ST"), state = StringSchema("Two-letter state"), trade = StringSchema("Construction trade"),
        issued_within_days = IntegerSchema(1, 3650, 7), minimum_project_value = NumberSchema(),
        commercial_only = new { type = "boolean", @default = false }, limit = IntegerSchema(1, 100, 25)
    };

    private static string[] WorkCategoryValues() => LiveAuthCore.Data.Entities.PermitSignal.PermitWorkCategories.All.OrderBy(item => item).ToArray();
    private static object StringSchema(string description) => new { type = "string", description };
    private static object IntegerSchema(int minimum, int maximum, int defaultValue) => new { type = "integer", minimum, maximum, @default = defaultValue };
    private static object NumberSchema() => new { type = "number", minimum = 0 };
    private static object DateSchema() => new { type = "string", format = "date-time" };
    private static JsonElement EmptyObject() => JsonDocument.Parse("{}").RootElement.Clone();
    private IActionResult RpcResult(JsonElement? id, object result) => Ok(new { jsonrpc = "2.0", id, result });
    private IActionResult RpcError(JsonElement? id, int code, string message, object? data = null)
        => Ok(new { jsonrpc = "2.0", id, error = new { code, message, data } });
}

public sealed class McpJsonRpcRequest
{
    public string Jsonrpc { get; set; } = string.Empty;
    public JsonElement? Id { get; set; }
    public string Method { get; set; } = string.Empty;
    public JsonElement? Params { get; set; }
}
