using System.Text.Json;
using Estacionamento.Application.Abstractions;
using Estacionamento.Application.Billing;
using Estacionamento.Application.Idempotency;
using Estacionamento.Application.Security;
using Estacionamento.Domain.Parking;

namespace Estacionamento.Application.Parking;

public sealed record VehicleCheckInCommand(
    Guid IdempotencyKey,
    string Plate,
    string Model,
    string OwnerName,
    string DocumentNumber,
    string EntryInspection,
    Guid ParkingSpaceId);

public sealed record VehicleCheckInResult(
    Guid AttendanceId,
    string PublicIdentifier,
    Guid ParkingSpaceId,
    string State,
    DateTimeOffset EnteredAtUtc,
    int HttpStatusCode = 201);

public enum VehicleCheckInStatus { Created, Invalid, Forbidden, SpaceNotFound, SpaceOccupied, DailyRateNotConfigured, IdempotencyConflict }
public sealed record VehicleCheckInOutcome(VehicleCheckInStatus Status, VehicleCheckInResult? Result = null, bool IsReplay = false);

public sealed record CheckInSpace(Guid Id, string Code, byte[] Version, bool IsOccupied);
public sealed record StoredCheckInResult(byte[] ContentHmac, int AlgorithmVersion, string ResultJson);

public sealed class IdempotencyKeyAlreadyExistsException(Exception? innerException = null)
    : Exception("An idempotency result with the same actor, operation, and key already exists.", innerException);
public sealed class ParkingSpaceOccupancyConflictException(Exception? innerException = null)
    : Exception("The parking space was claimed by another check-in.", innerException);
public sealed class VehicleCheckInRejectedException(VehicleCheckInStatus status) : Exception("The check-in precondition was not met.")
{
    public VehicleCheckInStatus Status { get; } = status;
}

public interface ICheckInStore
{
    Task<StoredCheckInResult?> FindIdempotentResultAsync(Guid actorId, Guid key, CancellationToken cancellationToken);
    Task<StoredCheckInResult?> ResolveCommittedResultAsync(Guid actorId, Guid key, CancellationToken cancellationToken);
    Task<CheckInSpace?> FindSpaceAsync(Guid spaceId, CancellationToken cancellationToken);
    Task ReserveIdempotencyKeyAsync(Guid actorId, Guid key, byte[] contentHmac, int algorithmVersion, CancellationToken cancellationToken);
    Task<bool> ClaimSpaceAsync(Guid spaceId, byte[] expectedVersion, CancellationToken cancellationToken);
    Task AddAttendanceAsync(Attendance attendance, string encryptedDocument, Guid dailyRateId, decimal dailyRateAmount,
        DateTimeOffset enteredAtUtc, Guid operatorId, CancellationToken cancellationToken);
    Task AddInspectionAsync(Guid attendanceId, string encryptedText, Guid actorId, DateTimeOffset recordedAtUtc,
        CancellationToken cancellationToken);
    Task AddOccupancyAsync(Guid spaceId, Guid attendanceId, DateTimeOffset startedAtUtc, Guid actorId,
        CancellationToken cancellationToken);
    Task CompleteIdempotencyResultAsync(Guid actorId, Guid key, VehicleCheckInResult result, CancellationToken cancellationToken);
}

public sealed class VehicleCheckIn(
    ICheckInStore store,
    IDailyRateDirectory rates,
    ICurrentActor actor,
    IUseCaseAuthorizer authorizer,
    IUnitOfWork unitOfWork,
    IFieldProtector fieldProtector,
    ICheckInContentHmac contentHmac,
    IAuditWriter auditWriter,
    IClock clock)
{
    public async Task<VehicleCheckInOutcome> ExecuteAsync(VehicleCheckInCommand command, CancellationToken cancellationToken)
    {
        if (actor.UserId is not Guid actorId || !authorizer.IsAllowed(actor.Roles, EmployeeAction.CreateAttendance))
        {
            return new VehicleCheckInOutcome(VehicleCheckInStatus.Forbidden);
        }

        if (command.IdempotencyKey == Guid.Empty)
        {
            return new VehicleCheckInOutcome(VehicleCheckInStatus.Invalid);
        }

        Attendance attendance;
        EntryInspection inspection;
        CanonicalCheckInContent canonicalContent;
        try
        {
            attendance = Attendance.Create(command.Plate, command.Model, command.OwnerName, command.DocumentNumber);
            inspection = EntryInspection.Create(command.EntryInspection, actorId, clock.UtcNow);
            canonicalContent = CanonicalCheckInContent.Create(
                command.Plate, command.Model, command.OwnerName, command.DocumentNumber,
                command.EntryInspection, command.ParkingSpaceId);
        }
        catch (ArgumentException)
        {
            return new VehicleCheckInOutcome(VehicleCheckInStatus.Invalid);
        }

        var digest = contentHmac.Compute(canonicalContent);
        try
        {
            return await unitOfWork.ExecuteAsync(async token =>
            {
                var existing = await store.FindIdempotentResultAsync(actorId, command.IdempotencyKey, token);
                if (existing is not null)
                {
                    return ResolveExisting(existing, canonicalContent);
                }

                await store.ReserveIdempotencyKeyAsync(
                    actorId, command.IdempotencyKey, digest.Hmac, digest.KeyVersion, token);

                var rate = await rates.ReadCurrentForAttendanceAsync(token);
                if (rate is null)
                {
                    throw new VehicleCheckInRejectedException(VehicleCheckInStatus.DailyRateNotConfigured);
                }

                var space = await store.FindSpaceAsync(command.ParkingSpaceId, token);
                if (space is null)
                {
                    throw new VehicleCheckInRejectedException(VehicleCheckInStatus.SpaceNotFound);
                }

                if (space.IsOccupied)
                {
                    throw new VehicleCheckInRejectedException(VehicleCheckInStatus.SpaceOccupied);
                }

                var enteredAtUtc = clock.UtcNow.ToUniversalTime();
                var encryptedDocument = fieldProtector.Protect(attendance.DocumentNumber, attendance.Id, "document", 1);
                var encryptedInspection = fieldProtector.Protect(inspection.Text, attendance.Id, "entry-inspection", 1);
                var result = new VehicleCheckInResult(attendance.Id, attendance.PublicIdentifier, space.Id, "active", enteredAtUtc);

                if (!await store.ClaimSpaceAsync(space.Id, space.Version, token))
                {
                    throw new VehicleCheckInRejectedException(VehicleCheckInStatus.SpaceOccupied);
                }

                await store.AddAttendanceAsync(attendance, encryptedDocument, rate.Id, rate.Amount, enteredAtUtc, actorId, token);
                await store.AddInspectionAsync(attendance.Id, encryptedInspection, actorId, enteredAtUtc, token);
                await store.AddOccupancyAsync(space.Id, attendance.Id, enteredAtUtc, actorId, token);
                await auditWriter.AppendAsync(actorId, "attendance.created", attendance.Id, enteredAtUtc,
                    new Dictionary<string, string> { ["attendanceId"] = attendance.Id.ToString("D") }, token);
                await auditWriter.AppendAsync(actorId, "space.assigned", attendance.Id, enteredAtUtc,
                    new Dictionary<string, string>
                    {
                        ["attendanceId"] = attendance.Id.ToString("D"),
                        ["spaceId"] = space.Id.ToString("D")
                    }, token);
                await auditWriter.AppendAsync(actorId, "daily_rate.captured", attendance.Id, enteredAtUtc,
                    new Dictionary<string, string>
                    {
                        ["attendanceId"] = attendance.Id.ToString("D"),
                        ["dailyRateId"] = rate.Id.ToString("D")
                    }, token);
                await store.CompleteIdempotencyResultAsync(actorId, command.IdempotencyKey, result, token);

                return new VehicleCheckInOutcome(VehicleCheckInStatus.Created, result);
            }, cancellationToken);
        }
        catch (IdempotencyKeyAlreadyExistsException)
        {
            var committed = await store.ResolveCommittedResultAsync(actorId, command.IdempotencyKey, cancellationToken);
            return committed is null
                ? throw new InvalidOperationException("The competing check-in did not leave a committed idempotent result.")
                : ResolveExisting(committed, canonicalContent);
        }
        catch (VehicleCheckInRejectedException exception)
        {
            return new VehicleCheckInOutcome(exception.Status);
        }
        catch (ParkingSpaceOccupancyConflictException)
        {
            return new VehicleCheckInOutcome(VehicleCheckInStatus.SpaceOccupied);
        }
    }

    private VehicleCheckInOutcome ResolveExisting(StoredCheckInResult existing, CanonicalCheckInContent content)
    {
        if (!contentHmac.Matches(content, existing.AlgorithmVersion, existing.ContentHmac))
        {
            return new VehicleCheckInOutcome(VehicleCheckInStatus.IdempotencyConflict);
        }

        var result = JsonSerializer.Deserialize<VehicleCheckInResult>(existing.ResultJson)
            ?? throw new InvalidOperationException("The committed idempotent check-in result is invalid.");
        return new VehicleCheckInOutcome(VehicleCheckInStatus.Created, result, IsReplay: true);
    }
}
