using Estacionamento.Application.Parking;
using Estacionamento.Infrastructure.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Estacionamento.Infrastructure.Persistence;

public sealed class EfSpaceDirectory(IdentityDbContext dbContext) : ISpaceDirectory
{
    public async Task<(IReadOnlyList<ParkingSpaceView> Items, int Total)> ListAsync(
        string? code, bool? occupied, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = dbContext.ParkingSpaces.AsNoTracking().AsQueryable();
        if (code is not null)
        {
            query = query.Where(space => space.Code == code);
        }

        var joined = query.Select(space => new
        {
            space.Id,
            space.Code,
            space.RowVersion,
            IsOccupied = dbContext.Occupancies.Any(occupancy => occupancy.SpaceId == space.Id && occupancy.EndedAtUtc == null)
        });
        if (occupied is not null)
        {
            joined = joined.Where(space => space.IsOccupied == occupied.Value);
        }

        var total = await joined.CountAsync(cancellationToken);
        var rows = await joined.OrderBy(space => space.Code).ThenBy(space => space.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var items = rows.Select(space => new ParkingSpaceView(
            space.Id, space.Code, space.IsOccupied, Convert.ToBase64String(space.RowVersion))).ToArray();
        return (items, total);
    }

    public async Task<SpaceCreateResult> CreateAsync(string normalizedCode, CancellationToken cancellationToken)
    {
        var row = new ParkingSpaceRow { Id = Guid.NewGuid(), Code = normalizedCode };
        dbContext.ParkingSpaces.Add(row);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            return new SpaceCreateResult(SpaceCreateStatus.Duplicate);
        }

        return new SpaceCreateResult(SpaceCreateStatus.Created,
            new ParkingSpaceView(row.Id, row.Code, false, Convert.ToBase64String(row.RowVersion)));
    }

    private static bool IsUniqueConstraintViolation(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: 2601 or 2627 })
            {
                return true;
            }
        }

        return false;
    }
}
