using Estacionamento.Application.Billing;
using Estacionamento.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace Estacionamento.Infrastructure.Persistence;

public sealed class EfDailyRateDirectory(IdentityDbContext dbContext) : IDailyRateDirectory
{
    public async Task<DailyRateConfigurationView> GetConfigurationAsync(CancellationToken cancellationToken)
    {
        var configuration = await dbContext.DailyRateConfigurations.AsNoTracking()
            .Include(item => item.CurrentRate)
            .SingleAsync(item => item.Id == 1, cancellationToken);
        return new DailyRateConfigurationView(
            configuration.CurrentRate is null ? null : View(configuration.CurrentRate),
            Convert.ToBase64String(configuration.RowVersion));
    }

    public async Task<DailyRatePage> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = dbContext.DailyRates.AsNoTracking();
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(rate => rate.PublishedAtUtc).ThenByDescending(rate => rate.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(cancellationToken);
        return new DailyRatePage(rows.Select(View).ToArray(), page, pageSize, total);
    }

    public async Task<CurrentRateSnapshot?> ReadCurrentForAttendanceAsync(CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("The current daily rate must be read inside the attendance transaction.");
        }

        var current = await dbContext.DailyRates.FromSqlRaw("""
                SELECT rate.*
                FROM [DailyRateConfiguration] AS configuration WITH (HOLDLOCK)
                INNER JOIN [DailyRates] AS rate WITH (HOLDLOCK)
                    ON rate.[Id] = configuration.[CurrentRateId]
                WHERE configuration.[Id] = 1
                """)
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        return current is null ? null : new CurrentRateSnapshot(current.Id, current.Amount, current.PublishedAtUtc);
    }

    public async Task<DailyRateView> PublishAsync(
        decimal amount, string baseVersion, Guid publishedById, DateTimeOffset publishedAtUtc, CancellationToken cancellationToken)
    {
        var configuration = await dbContext.DailyRateConfigurations.SingleAsync(item => item.Id == 1, cancellationToken);
        var expected = Convert.FromBase64String(baseVersion);
        if (!configuration.RowVersion.SequenceEqual(expected))
        {
            throw new DailyRateConcurrencyException();
        }

        var row = new DailyRateRow
        {
            Id = Guid.NewGuid(),
            Amount = amount,
            PublishedAtUtc = publishedAtUtc.ToUniversalTime(),
            PublishedById = publishedById
        };
        dbContext.DailyRates.Add(row);
        configuration.CurrentRateId = row.Id;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DailyRateConcurrencyException();
        }

        return View(row);
    }

    private static DailyRateView View(DailyRateRow row) =>
        new(row.Id, row.Amount, row.PublishedAtUtc, row.PublishedById);
}
