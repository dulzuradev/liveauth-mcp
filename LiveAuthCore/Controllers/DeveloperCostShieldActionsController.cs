using System.Security.Claims;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Models.CostShield;
using LiveAuthCore.Services.CostShield;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using JwtRegisteredClaimNames = System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames;

namespace LiveAuthCore.Controllers;

[ApiController]
[Route("api/dev/projects/{projectId:guid}/costshield/actions")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class DeveloperCostShieldActionsController : ControllerBase
{
    private readonly IProtectedActionService _actions;

    public DeveloperCostShieldActionsController(IProtectedActionService actions)
    {
        _actions = actions;
    }

    [HttpGet]
    public async Task<ActionResult<ProtectedActionListResponse>> ListActions(
        Guid projectId,
        [FromQuery] string? environment = null,
        CancellationToken ct = default)
    {
        var result = await _actions.ListAsync(
            projectId,
            GetDeveloperId(),
            User.IsInRole("Admin"),
            environment,
            ct);

        return result.Status switch
        {
            ProtectedActionResultStatus.Found => Ok(new ProtectedActionListResponse(
                result.Actions.Select(ToDto).ToList())),
            ProtectedActionResultStatus.Invalid => ValidationProblem(
                new ValidationProblemDetails(ToValidationErrors(result.Errors))),
            _ => NotFound()
        };
    }

    [HttpGet("{actionId:guid}")]
    public async Task<ActionResult<ProtectedActionDto>> GetAction(
        Guid projectId,
        Guid actionId,
        CancellationToken ct = default)
    {
        var result = await _actions.GetAsync(
            projectId,
            actionId,
            GetDeveloperId(),
            User.IsInRole("Admin"),
            ct);

        var action = result.Actions.SingleOrDefault();
        return action == null ? NotFound() : Ok(ToDto(action));
    }

    [HttpPost]
    public async Task<ActionResult<ProtectedActionDto>> CreateAction(
        Guid projectId,
        [FromBody] UpsertProtectedActionRequest request,
        CancellationToken ct = default)
    {
        var result = await _actions.CreateAsync(
            projectId,
            GetDeveloperId(),
            User.IsInRole("Admin"),
            request,
            ct);

        if (result.Status == ProtectedActionResultStatus.Created)
        {
            var dto = ToDto(result.Action!);
            return CreatedAtAction(
                nameof(GetAction),
                new { projectId, actionId = dto.Id },
                dto);
        }

        return MapWriteError(result);
    }

    [HttpPut("{actionId:guid}")]
    public async Task<ActionResult<ProtectedActionDto>> UpdateAction(
        Guid projectId,
        Guid actionId,
        [FromBody] UpsertProtectedActionRequest request,
        CancellationToken ct = default)
    {
        var result = await _actions.UpdateAsync(
            projectId,
            actionId,
            GetDeveloperId(),
            User.IsInRole("Admin"),
            request,
            ct);

        return result.Status == ProtectedActionResultStatus.Updated
            ? Ok(ToDto(result.Action!))
            : MapWriteError(result);
    }

    [HttpDelete("{actionId:guid}")]
    public async Task<IActionResult> DeleteAction(
        Guid projectId,
        Guid actionId,
        CancellationToken ct = default)
    {
        var result = await _actions.DeleteAsync(
            projectId,
            actionId,
            GetDeveloperId(),
            User.IsInRole("Admin"),
            ct);

        return result.Status == ProtectedActionResultStatus.Deleted
            ? NoContent()
            : MapWriteError(result);
    }

    private ActionResult MapWriteError(ProtectedActionWriteResult result)
    {
        return result.Status switch
        {
            ProtectedActionResultStatus.NotFound => NotFound(),
            ProtectedActionResultStatus.Invalid => ValidationProblem(
                new ValidationProblemDetails(ToValidationErrors(result.Errors))),
            ProtectedActionResultStatus.Conflict => Conflict(new
            {
                error = "protected_action_name_conflict",
                message = "An action with this name already exists in the selected environment."
            }),
            ProtectedActionResultStatus.PlanLimitReached => StatusCode(
                StatusCodes.Status402PaymentRequired,
                new
                {
                    error = "protected_action_limit_reached",
                    message = "This project's plan has reached its protected action limit.",
                    limit = result.Limit
                }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    private Guid GetDeveloperId()
    {
        var raw =
            User.FindFirst("userId")?.Value ??
            User.FindFirst("developer_id")?.Value ??
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
            User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (!Guid.TryParse(raw, out var developerId))
            throw new UnauthorizedAccessException("Invalid developer identity");

        return developerId;
    }

    private static Dictionary<string, string[]> ToValidationErrors(
        IReadOnlyDictionary<string, string[]>? errors)
    {
        return errors?.ToDictionary(pair => pair.Key, pair => pair.Value) ??
               new Dictionary<string, string[]>
               {
                   ["request"] = new[] { "The protected action configuration is invalid." }
               };
    }

    private static ProtectedActionDto ToDto(ProtectedAction action)
    {
        return new ProtectedActionDto(
            action.Id,
            action.ProjectId,
            action.Environment,
            action.Name,
            action.DisplayName,
            action.Description,
            action.IsEnabled,
            action.BaseDifficulty,
            action.SuspiciousDifficulty,
            action.MaximumDifficulty,
            action.AnonymousRequestLimit,
            action.AnonymousLimitWindowSeconds,
            action.AuthenticatedRequestLimit,
            action.AuthenticatedLimitWindowSeconds,
            action.RequireSingleUseToken,
            action.TokenLifetimeSeconds,
            action.AllowedOrigins.ToList(),
            action.FailureBehavior,
            action.AllowLightningFallback,
            action.LightningPriceSats,
            action.LightningFallbackMode,
            action.LightningBypassesProofOfWork,
            action.EstimatedCostPerExecution,
            action.ConfigurationVersion,
            action.CreatedAt,
            action.UpdatedAt);
    }
}
