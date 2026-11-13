using Estacionamento.Application.Billing;
using Estacionamento.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Estacionamento.Api.Controllers;

[ApiController]
[Route("api/v1/daily-rates")]
[Authorize(Roles = EmployeeRoles.Administrator)]
public sealed class DailyRatesController(DailyRateAdministration administration) : ControllerBase
{
    [HttpGet("current")]
    [ProducesResponseType(typeof(DailyRateConfigurationView), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Current(CancellationToken cancellationToken)
    {
        var configuration = await administration.GetConfigurationAsync(cancellationToken);
        return configuration is null ? Forbid() : Ok(configuration);
    }

    [HttpGet]
    [ProducesResponseType(typeof(DailyRatePage), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var result = await administration.ListAsync(page, pageSize, cancellationToken);
        if (result is null)
        {
            return page < 1 || pageSize is < 1 or > 100
                ? BadRequest(new { code = "invalid_pagination" })
                : Forbid();
        }

        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(DailyRatePublishedResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Publish([FromBody] PublishDailyRateRequest request, CancellationToken cancellationToken)
    {
        var result = await administration.PublishAsync(new PublishDailyRateCommand(request.Amount, request.BaseVersion), cancellationToken);
        return result switch
        {
            DailyRatePublishStatus.Published => CreatedAtAction(nameof(Current), routeValues: null, value: new DailyRatePublishedResponse("published")),
            DailyRatePublishStatus.Invalid => BadRequest(new { code = "invalid_daily_rate" }),
            DailyRatePublishStatus.Conflict => Conflict(new { code = "stale_daily_rate_version" }),
            DailyRatePublishStatus.Forbidden => Forbid(),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }
}

public sealed record PublishDailyRateRequest(decimal Amount, string BaseVersion);
public sealed record DailyRatePublishedResponse(string Status);
