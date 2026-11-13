using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Estacionamento.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Estacionamento.Infrastructure.Identity;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : IdentityDbContext<EmployeeAccount, EmployeeRole, Guid>(options)
{
    public DbSet<ParkingSpaceRow> ParkingSpaces => Set<ParkingSpaceRow>();
    public DbSet<DailyRateRow> DailyRates => Set<DailyRateRow>();
    public DbSet<DailyRateConfigurationRow> DailyRateConfigurations => Set<DailyRateConfigurationRow>();
    public DbSet<AccessAdministrationControlRow> AccessAdministrationControls => Set<AccessAdministrationControlRow>();
    public DbSet<AttendanceRow> Attendances => Set<AttendanceRow>();
    public DbSet<EntryInspectionRow> EntryInspections => Set<EntryInspectionRow>();
    public DbSet<OccupancyRow> Occupancies => Set<OccupancyRow>();
    public DbSet<AuditEventRow> AuditEvents => Set<AuditEventRow>();
    public DbSet<IdempotentResultRow> IdempotentResults => Set<IdempotentResultRow>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<EmployeeAccount>().HasIndex(user => user.NormalizedEmail).IsUnique().HasDatabaseName("EmailIndex");
        builder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureAuditEventsAreAppendOnly();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnsureAuditEventsAreAppendOnly();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void EnsureAuditEventsAreAppendOnly()
    {
        if (ChangeTracker.Entries<AuditEventRow>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Audit events are append-only and cannot be changed or deleted.");
        }
    }
}
