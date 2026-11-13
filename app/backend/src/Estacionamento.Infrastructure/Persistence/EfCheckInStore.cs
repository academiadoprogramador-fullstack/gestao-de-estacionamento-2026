using System.Text.Json;
using Estacionamento.Application.Parking;
using Estacionamento.Domain.Parking;
using Estacionamento.Infrastructure.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Estacionamento.Infrastructure.Persistence;

public sealed class EfCheckInStore(IdentityDbContext dbContext, IDbContextFactory<IdentityDbContext> dbContextFactory) : ICheckInStore
{
    private IdempotentResultRow? reservation;

    public async Task<StoredCheckInResult?> FindIdempotentResultAsync(Guid actorId, Guid key, CancellationToken cancellationToken)
    {
        var row = await dbContext.IdempotentResults.AsNoTracking()
            .SingleOrDefaultAsync(result => result.ActorId == actorId
                && result.Operation == "vehicle.check-in" && result.Key == key, cancellationToken);
        return row is null ? null : Stored(row);
    }

    public async Task<StoredCheckInResult?> ResolveCommittedResultAsync(Guid actorId, Guid key, CancellationToken cancellationToken)
    {
        await using var freshContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var row = await freshContext.IdempotentResults.AsNoTracking()
            .SingleOrDefaultAsync(result => result.ActorId == actorId
                && result.Operation == "vehicle.check-in" && result.Key == key, cancellationToken);
        return row is null ? null : Stored(row);
    }

    public async Task<CheckInSpace?> FindSpaceAsync(Guid spaceId, CancellationToken cancellationToken)
    {
        var space = await dbContext.ParkingSpaces.AsNoTracking()
            .Where(item => item.Id == spaceId)
            .Select(item => new
            {
                item.Id,
                item.Code,
                item.RowVersion,
                IsOccupied = dbContext.Occupancies.Any(occupancy =>
                    occupancy.SpaceId == item.Id && occupancy.EndedAtUtc == null)
            })
            .SingleOrDefaultAsync(cancellationToken);

        return space is null ? null : new CheckInSpace(space.Id, space.Code, space.RowVersion, space.IsOccupied);
    }

    public async Task<bool> ClaimSpaceAsync(Guid spaceId, byte[] expectedVersion, CancellationToken cancellationToken)
    {
        var updated = await dbContext.ParkingSpaces
            .Where(space => space.Id == spaceId && space.RowVersion == expectedVersion)
            .ExecuteUpdateAsync(setters => setters.SetProperty(space => space.Code, space => space.Code), cancellationToken);
        return updated == 1;
    }

    public async Task ReserveIdempotencyKeyAsync(
        Guid actorId, Guid key, byte[] contentHmac, int algorithmVersion, CancellationToken cancellationToken)
    {
        reservation = new IdempotentResultRow
        {
            Id = Guid.NewGuid(),
            ActorId = actorId,
            Operation = "vehicle.check-in",
            Key = key,
            ContentHmac = contentHmac,
            AlgorithmVersion = algorithmVersion,
            ResultJson = "{}"
        };
        dbContext.IdempotentResults.Add(reservation);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsIdempotencyKeyViolation(exception))
        {
            throw new IdempotencyKeyAlreadyExistsException(exception);
        }
    }

    public async Task AddAttendanceAsync(
        Attendance attendance,
        string encryptedDocument,
        Guid dailyRateId,
        decimal dailyRateAmount,
        DateTimeOffset enteredAtUtc,
        Guid operatorId,
        CancellationToken cancellationToken)
    {
        dbContext.Attendances.Add(new AttendanceRow
        {
            Id = attendance.Id,
            PublicIdentifier = attendance.PublicIdentifier,
            NormalizedPlate = attendance.Plate.Value,
            Model = attendance.Model,
            OwnerName = attendance.OwnerName,
            EncryptedDocument = encryptedDocument,
            DailyRateId = dailyRateId,
            DailyRateSnapshot = dailyRateAmount,
            EnteredAtUtc = enteredAtUtc,
            OperatorId = operatorId,
            State = "active"
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task AddInspectionAsync(
        Guid attendanceId, string encryptedText, Guid actorId, DateTimeOffset recordedAtUtc,
        CancellationToken cancellationToken)
    {
        dbContext.EntryInspections.Add(new EntryInspectionRow
        {
            Id = Guid.NewGuid(),
            AttendanceId = attendanceId,
            EncryptedText = encryptedText,
            ActorId = actorId,
            RecordedAtUtc = recordedAtUtc
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task AddOccupancyAsync(
        Guid spaceId, Guid attendanceId, DateTimeOffset startedAtUtc, Guid actorId,
        CancellationToken cancellationToken)
    {
        dbContext.Occupancies.Add(new OccupancyRow
        {
            Id = Guid.NewGuid(),
            SpaceId = spaceId,
            AttendanceId = attendanceId,
            StartedAtUtc = startedAtUtc,
            ActorId = actorId
        });
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsSpaceOccupancyViolation(exception))
        {
            throw new ParkingSpaceOccupancyConflictException(exception);
        }
    }

    public async Task CompleteIdempotencyResultAsync(
        Guid actorId, Guid key, VehicleCheckInResult result, CancellationToken cancellationToken)
    {
        if (reservation is null || reservation.ActorId != actorId || reservation.Key != key
            || reservation.AttendanceId is not null)
        {
            throw new InvalidOperationException("The idempotency key must be reserved before the result is completed.");
        }

        reservation.ResultJson = JsonSerializer.Serialize(result);
        reservation.AttendanceId = result.AttendanceId;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static StoredCheckInResult Stored(IdempotentResultRow row) =>
        new(row.ContentHmac, row.AlgorithmVersion, row.ResultJson);

    private static bool IsIdempotencyKeyViolation(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: 2601 or 2627 } sqlException
                && sqlException.Message.Contains("IX_IdempotentResults_ActorId_Operation_Key", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSpaceOccupancyViolation(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: 2601 or 2627 } sqlException
                && sqlException.Message.Contains("IX_Occupancies_SpaceId", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
