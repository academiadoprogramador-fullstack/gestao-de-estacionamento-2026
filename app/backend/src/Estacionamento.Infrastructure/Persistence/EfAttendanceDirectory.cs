using Estacionamento.Application.Parking;
using Estacionamento.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace Estacionamento.Infrastructure.Persistence;

public sealed class EfAttendanceDirectory(IdentityDbContext dbContext) : IAttendanceDirectory
{
    public Task<string?> GetEncryptedDocumentAsync(Guid attendanceId, CancellationToken cancellationToken) =>
        dbContext.Attendances.AsNoTracking()
            .Where(attendance => attendance.Id == attendanceId)
            .Select(attendance => attendance.EncryptedDocument)
            .SingleOrDefaultAsync(cancellationToken);

    public Task<string?> GetEncryptedEntryInspectionAsync(Guid attendanceId, CancellationToken cancellationToken) =>
        dbContext.EntryInspections.AsNoTracking()
            .Where(inspection => inspection.AttendanceId == attendanceId)
            .Select(inspection => inspection.EncryptedText)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<AttendanceDetailData?> GetDetailAsync(Guid attendanceId, CancellationToken cancellationToken)
    {
        return await (from attendance in dbContext.Attendances.AsNoTracking()
                      join occupancy in dbContext.Occupancies.AsNoTracking()
                          on attendance.Id equals occupancy.AttendanceId
                      join space in dbContext.ParkingSpaces.AsNoTracking()
                          on occupancy.SpaceId equals space.Id
                      where attendance.Id == attendanceId
                      select new AttendanceDetailData(
                          attendance.Id,
                          attendance.PublicIdentifier,
                          attendance.NormalizedPlate,
                          attendance.Model,
                          attendance.OwnerName,
                          attendance.EnteredAtUtc,
                          attendance.State,
                          attendance.OperatorId,
                          space.Id,
                          space.Code,
                          attendance.DailyRateId,
                          attendance.DailyRateSnapshot))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<AttendanceSearchItem> Items, int Total)> SearchAsync(
        string? normalizedPlate,
        string? normalizedIdentifier,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Attendances.AsNoTracking();
        if (normalizedPlate is not null)
        {
            query = query.Where(attendance => attendance.NormalizedPlate == normalizedPlate);
        }
        else if (normalizedIdentifier is not null)
        {
            query = query.Where(attendance => attendance.PublicIdentifier == normalizedIdentifier);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await (from attendance in query
                          join occupancy in dbContext.Occupancies.AsNoTracking()
                              on attendance.Id equals occupancy.AttendanceId
                          join space in dbContext.ParkingSpaces.AsNoTracking()
                              on occupancy.SpaceId equals space.Id
                          select new
                          {
                              attendance.Id,
                              attendance.PublicIdentifier,
                              Plate = attendance.NormalizedPlate,
                              attendance.Model,
                              attendance.EnteredAtUtc,
                              attendance.State,
                              ParkingSpaceId = space.Id,
                              ParkingSpaceCode = space.Code
                          })
            .OrderByDescending(item => item.EnteredAtUtc)
            .ThenBy(item => item.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = rows.Select(item => new AttendanceSearchItem(
            item.Id,
            item.PublicIdentifier,
            item.Plate,
            item.Model,
            item.EnteredAtUtc,
            item.State,
            item.ParkingSpaceId,
            item.ParkingSpaceCode)).ToArray();

        return (items, total);
    }
}
