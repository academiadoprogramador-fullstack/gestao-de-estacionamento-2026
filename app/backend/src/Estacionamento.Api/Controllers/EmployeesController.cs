using Estacionamento.Application.Employees;
using Estacionamento.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Estacionamento.Api.Controllers;

[ApiController]
[Route("api/v1/employees")]
[Authorize(Roles = EmployeeRoles.Administrator)]
public sealed class EmployeesController(EmployeeAdministration administration) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(EmployeePageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var result = await administration.ListAsync(page, pageSize, cancellationToken);
        return result.Status switch
        {
            EmployeeListStatus.Listed => Ok(new EmployeePageResponse(result.Items, page, pageSize, result.Total)),
            EmployeeListStatus.Invalid => BadRequest(new { code = "invalid_pagination" }),
            _ => Forbid()
        };
    }

    [HttpPost]
    [ProducesResponseType(typeof(EmployeeListItem), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateEmployeeRequest request, CancellationToken cancellationToken)
    {
        var result = await administration.CreateAsync(
            new CreateEmployeeCommand(request.Email, request.InitialPassword, request.Roles), cancellationToken);
        return result.Status switch
        {
            EmployeeCreateStatus.Created => StatusCode(StatusCodes.Status201Created, result.Employee),
            EmployeeCreateStatus.DuplicateEmail => Conflict(new { code = "duplicate_email" }),
            EmployeeCreateStatus.Invalid => BadRequest(new { code = "invalid_employee" }),
            EmployeeCreateStatus.Forbidden => Forbid(),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    [HttpPut("{id:guid}/access")]
    [ProducesResponseType(typeof(EmployeeAccessResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAccess(Guid id, [FromBody] UpdateEmployeeAccessRequest request, CancellationToken cancellationToken)
    {
        var result = await administration.UpdateAccessAsync(
            new UpdateEmployeeAccessCommand(id, request.ConcurrencyStamp, request.IsActive, request.Roles), cancellationToken);
        return result.Status switch
        {
            EmployeeAccessStatus.Updated => Ok(new EmployeeAccessResponse(result.Employee!, result.ConcurrencyStamp!)),
            EmployeeAccessStatus.Invalid => BadRequest(new { code = "invalid_access" }),
            EmployeeAccessStatus.NotFound => NotFound(new { code = "employee_not_found" }),
            EmployeeAccessStatus.Conflict => Conflict(new { code = "stale_employee" }),
            EmployeeAccessStatus.LastAdministrator => Conflict(new { code = "last_active_administrator" }),
            EmployeeAccessStatus.Forbidden => Forbid(),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }
}

public sealed record CreateEmployeeRequest(string Email, string InitialPassword, IReadOnlyCollection<string> Roles);
public sealed record UpdateEmployeeAccessRequest(string ConcurrencyStamp, bool IsActive, IReadOnlyCollection<string> Roles);
public sealed record EmployeePageResponse(IReadOnlyList<EmployeeListItem> Items, int Page, int PageSize, int Total);
public sealed record EmployeeAccessResponse(EmployeeListItem Employee, string ConcurrencyStamp);
