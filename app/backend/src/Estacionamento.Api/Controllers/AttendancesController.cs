using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Estacionamento.Application.Idempotency;
using Estacionamento.Application.Parking;
using Estacionamento.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Estacionamento.Api.Controllers;

[ApiController]
[Route("api/v1/attendances")]
public sealed class AttendancesController(VehicleCheckIn checkIn, AttendanceQuery attendanceQuery) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = EmployeeRoles.Cashier + "," + EmployeeRoles.Administrator)]
    [ProducesResponseType(typeof(AttendanceCreatedResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody] CreateAttendanceRequest request,
        CancellationToken cancellationToken)
    {
        if (!IdempotencyKey.TryParse(idempotencyKey, out var key))
        {
            return BadRequest(new { code = "invalid_idempotency_key" });
        }

        var outcome = await checkIn.ExecuteAsync(new VehicleCheckInCommand(
            key.Value,
            request.Plate,
            request.Model,
            request.OwnerName,
            request.DocumentNumber,
            request.EntryInspection,
            request.ParkingSpaceId), cancellationToken);

        return outcome.Status switch
        {
            VehicleCheckInStatus.Created => Created(
                $"/api/v1/attendances/{outcome.Result!.AttendanceId:D}",
                new AttendanceCreatedResponse(
                    outcome.Result.AttendanceId,
                    outcome.Result.PublicIdentifier,
                    outcome.Result.ParkingSpaceId,
                    outcome.Result.State,
                    outcome.Result.EnteredAtUtc)),
            VehicleCheckInStatus.Invalid => BadRequest(new { code = "invalid_check_in" }),
            VehicleCheckInStatus.SpaceNotFound => NotFound(new { code = "space_not_found" }),
            VehicleCheckInStatus.SpaceOccupied => Conflict(new { code = "space_occupied" }),
            VehicleCheckInStatus.IdempotencyConflict => Conflict(new { code = "idempotency_key_reused" }),
            VehicleCheckInStatus.DailyRateNotConfigured => UnprocessableEntity(new { code = "daily_rate_not_configured" }),
            VehicleCheckInStatus.Forbidden => Forbid(),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    [HttpGet]
    [Authorize(Roles = EmployeeRoles.Valet + "," + EmployeeRoles.Cashier + "," + EmployeeRoles.Administrator)]
    [ProducesResponseType(typeof(AttendanceSearchPage), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Search(
        [FromQuery] string? plate,
        [FromQuery] string? identifier,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await attendanceQuery.SearchAsync(plate, identifier, page, pageSize, cancellationToken);
        return result.Status switch
        {
            AttendanceSearchStatus.Found => Ok(result.Page),
            AttendanceSearchStatus.Invalid => BadRequest(new { code = "invalid_attendance_query" }),
            _ => Forbid()
        };
    }

    [HttpGet("{attendanceId:guid}")]
    [Authorize(Roles = EmployeeRoles.Valet + "," + EmployeeRoles.Cashier + "," + EmployeeRoles.Administrator)]
    [ProducesResponseType(typeof(AttendanceDetailView), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Detail(Guid attendanceId, CancellationToken cancellationToken)
    {
        var result = await attendanceQuery.GetDetailAsync(attendanceId, cancellationToken);
        return result.Status switch
        {
            AttendanceDetailStatus.Found => Ok(result.Detail),
            AttendanceDetailStatus.Invalid => BadRequest(new { code = "invalid_attendance_id" }),
            AttendanceDetailStatus.NotFound => NotFound(new { code = "attendance_not_found" }),
            _ => Forbid()
        };
    }

    [HttpGet("{attendanceId:guid}/history")]
    [Authorize(Roles = EmployeeRoles.Valet + "," + EmployeeRoles.Cashier + "," + EmployeeRoles.Administrator)]
    [ProducesResponseType(typeof(AttendanceHistoryPage), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> History(
        Guid attendanceId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await attendanceQuery.GetHistoryAsync(attendanceId, page, pageSize, cancellationToken);
        return result.Status switch
        {
            AttendanceDetailStatus.Found => Ok(result.History),
            AttendanceDetailStatus.Invalid => BadRequest(new { code = "invalid_attendance_history_query" }),
            AttendanceDetailStatus.NotFound => NotFound(new { code = "attendance_not_found" }),
            _ => Forbid()
        };
    }

    [HttpGet("{attendanceId:guid}/document")]
    [Authorize(Roles = EmployeeRoles.Cashier + "," + EmployeeRoles.Administrator)]
    [ProducesResponseType(typeof(SensitiveFieldResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReadDocument(Guid attendanceId, CancellationToken cancellationToken)
    {
        var result = await attendanceQuery.ReadDocumentAsync(attendanceId, cancellationToken);
        return result.Status switch
        {
            SensitiveFieldStatus.Read => Ok(new SensitiveFieldResponse(result.Value!)),
            SensitiveFieldStatus.Invalid => BadRequest(new { code = "invalid_attendance_id" }),
            SensitiveFieldStatus.NotFound => NotFound(new { code = "attendance_not_found" }),
            _ => Forbid()
        };
    }

    [HttpGet("{attendanceId:guid}/entry-inspection")]
    [Authorize(Roles = EmployeeRoles.Valet + "," + EmployeeRoles.Cashier + "," + EmployeeRoles.Administrator)]
    [ProducesResponseType(typeof(SensitiveFieldResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReadEntryInspection(Guid attendanceId, CancellationToken cancellationToken)
    {
        var result = await attendanceQuery.ReadEntryInspectionAsync(attendanceId, cancellationToken);
        return result.Status switch
        {
            SensitiveFieldStatus.Read => Ok(new SensitiveFieldResponse(result.Value!)),
            SensitiveFieldStatus.Invalid => BadRequest(new { code = "invalid_attendance_id" }),
            SensitiveFieldStatus.NotFound => NotFound(new { code = "attendance_not_found" }),
            _ => Forbid()
        };
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateAttendanceRequest
{
    [Required]
    public string Plate { get; init; } = string.Empty;

    [Required, StringLength(120, MinimumLength = 1)]
    public string Model { get; init; } = string.Empty;

    [Required, StringLength(200, MinimumLength = 1)]
    public string OwnerName { get; init; } = string.Empty;

    [Required, StringLength(80, MinimumLength = 1)]
    public string DocumentNumber { get; init; } = string.Empty;

    [Required, StringLength(4000, MinimumLength = 1)]
    public string EntryInspection { get; init; } = string.Empty;

    public Guid ParkingSpaceId { get; init; }
}

public sealed record AttendanceCreatedResponse(
    Guid AttendanceId,
    string PublicIdentifier,
    Guid ParkingSpaceId,
    string State,
    DateTimeOffset EnteredAtUtc);

public sealed record SensitiveFieldResponse(string Value);
