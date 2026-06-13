using LiveAuthCore.Models;
using LiveAuthCore.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LiveAuthCore.Controllers;

[ApiController]
[Route("api/admin/settings/lightning-fees")]
[Authorize(Roles = "Admin")]
public class AdminLightningFeeSettingsController : ControllerBase
{
    private readonly LightningFeeSettingsService _settings;

    public AdminLightningFeeSettingsController(LightningFeeSettingsService settings)
    {
        _settings = settings;
    }

    [HttpGet]
    public async Task<ActionResult<LightningFeeSettingsResponse>> Get(CancellationToken ct)
    {
        return Ok(await _settings.GetResponseAsync(ct));
    }

    [HttpPut]
    public async Task<ActionResult<LightningFeeSettingsResponse>> Update(
        [FromBody] UpdateLightningFeeSettingsRequest request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await _settings.UpdateAsync(request, ct));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}

[ApiController]
[Route("api/dev/settings/lightning-fees")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class DeveloperLightningFeeSettingsController : ControllerBase
{
    private readonly LightningFeeSettingsService _settings;

    public DeveloperLightningFeeSettingsController(LightningFeeSettingsService settings)
    {
        _settings = settings;
    }

    [HttpGet]
    public async Task<ActionResult<LightningFeeSettingsResponse>> Get(CancellationToken ct)
    {
        return Ok(await _settings.GetResponseAsync(ct));
    }
}
