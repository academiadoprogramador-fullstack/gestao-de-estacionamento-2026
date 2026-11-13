using Estacionamento.Application.Parking;
using Estacionamento.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Estacionamento.Api.Controllers;

[ApiController]
[Route("api/v1/spaces")]
public sealed class SpacesController(SpaceAvailability spaces) : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = EmployeeRoles.Valet + "," + EmployeeRoles.Cashier + "," + EmployeeRoles.Administrator)]
    [ProducesResponseType(typeof(SpacePageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] string? code,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await spaces.ListAsync(code, status, page, pageSize, cancellationToken);
        return result.Status switch
        {
            SpaceQueryStatus.Listed => Ok(new SpacePageResponse(result.Items, page, pageSize, result.Total)),
            SpaceQueryStatus.Invalid => BadRequest(new { code = "invalid_space_query" }),
            _ => Forbid()
        };
    }

    [HttpPost]
    [Authorize(Roles = EmployeeRoles.Administrator)]
    [ProducesResponseType(typeof(ParkingSpaceView), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateSpaceRequest request, CancellationToken cancellationToken)
    {
        var result = await spaces.CreateAsync(new CreateParkingSpaceCommand(request.Code), cancellationToken);
        return result.Status switch
        {
            SpaceCreateStatus.Created => StatusCode(StatusCodes.Status201Created, result.Space),
            SpaceCreateStatus.Invalid => BadRequest(new { code = "invalid_space_code" }),
            SpaceCreateStatus.Duplicate => Conflict(new { code = "duplicate_space_code" }),
            SpaceCreateStatus.Forbidden => Forbid(),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }
}

public sealed record CreateSpaceRequest(string Code);
public sealed record SpacePageResponse(IReadOnlyList<ParkingSpaceView> Items, int Page, int PageSize, int Total);
