using Estacionamento.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Estacionamento.Infrastructure.Persistence;

internal sealed class ParkingSpaceConfiguration : IEntityTypeConfiguration<ParkingSpaceRow>
{
    public void Configure(EntityTypeBuilder<ParkingSpaceRow> builder)
    {
        builder.ToTable("ParkingSpaces");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(32).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
        builder.Property(x => x.RowVersion).IsRowVersion();
    }
}

internal sealed class DailyRateConfiguration : IEntityTypeConfiguration<DailyRateRow>
{
    public void Configure(EntityTypeBuilder<DailyRateRow> builder)
    {
        builder.ToTable("DailyRates");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.PublishedAtUtc).HasColumnType("datetimeoffset");
        builder.HasOne<EmployeeAccount>().WithMany().HasForeignKey(x => x.PublishedById).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DailyRateConfigurationRowConfiguration : IEntityTypeConfiguration<DailyRateConfigurationRow>
{
    public void Configure(EntityTypeBuilder<DailyRateConfigurationRow> builder)
    {
        builder.ToTable("DailyRateConfiguration");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasOne(x => x.CurrentRate).WithMany().HasForeignKey(x => x.CurrentRateId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AccessAdministrationControlConfiguration : IEntityTypeConfiguration<AccessAdministrationControlRow>
{
    public void Configure(EntityTypeBuilder<AccessAdministrationControlRow> builder)
    {
        builder.ToTable("AccessAdministrationControl");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.RowVersion).IsRowVersion();
    }
}

internal sealed class AttendanceConfiguration : IEntityTypeConfiguration<AttendanceRow>
{
    public void Configure(EntityTypeBuilder<AttendanceRow> builder)
    {
        builder.ToTable("Attendances");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PublicIdentifier).HasMaxLength(32).IsRequired();
        builder.HasIndex(x => x.PublicIdentifier).IsUnique();
        builder.Property(x => x.NormalizedPlate).HasMaxLength(16).IsRequired();
        builder.HasIndex(x => new { x.NormalizedPlate, x.EnteredAtUtc, x.Id });
        builder.Property(x => x.Model).HasMaxLength(120).IsRequired();
        builder.Property(x => x.OwnerName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.EncryptedDocument).HasMaxLength(2048).IsRequired();
        builder.Property(x => x.DailyRateSnapshot).HasPrecision(18, 2);
        builder.Property(x => x.EnteredAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.State).HasMaxLength(32).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasOne<DailyRateRow>().WithMany().HasForeignKey(x => x.DailyRateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EmployeeAccount>().WithMany().HasForeignKey(x => x.OperatorId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EntryInspectionConfiguration : IEntityTypeConfiguration<EntryInspectionRow>
{
    public void Configure(EntityTypeBuilder<EntryInspectionRow> builder)
    {
        builder.ToTable("EntryInspections");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EncryptedText).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.RecordedAtUtc).HasColumnType("datetimeoffset");
        builder.HasIndex(x => x.AttendanceId).IsUnique();
        builder.HasOne<AttendanceRow>().WithMany().HasForeignKey(x => x.AttendanceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EmployeeAccount>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class OccupancyConfiguration : IEntityTypeConfiguration<OccupancyRow>
{
    public void Configure(EntityTypeBuilder<OccupancyRow> builder)
    {
        builder.ToTable("Occupancies");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.StartedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.EndedAtUtc).HasColumnType("datetimeoffset");
        builder.HasIndex(x => x.SpaceId).IsUnique().HasFilter("[EndedAtUtc] IS NULL");
        builder.HasIndex(x => x.AttendanceId).IsUnique();
        builder.HasOne<ParkingSpaceRow>().WithMany().HasForeignKey(x => x.SpaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AttendanceRow>().WithMany().HasForeignKey(x => x.AttendanceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EmployeeAccount>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEventRow>
{
    public void Configure(EntityTypeBuilder<AuditEventRow> builder)
    {
        builder.ToTable("AuditEvents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Action).HasMaxLength(80).IsRequired();
        builder.Property(x => x.MetadataJson).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.OccurredAtUtc).HasColumnType("datetimeoffset");
        builder.HasIndex(x => new { x.AttendanceId, x.OccurredAtUtc, x.Id });
        builder.HasOne<EmployeeAccount>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AttendanceRow>().WithMany().HasForeignKey(x => x.AttendanceId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class IdempotentResultConfiguration : IEntityTypeConfiguration<IdempotentResultRow>
{
    public void Configure(EntityTypeBuilder<IdempotentResultRow> builder)
    {
        builder.ToTable("IdempotentResults");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Operation).HasMaxLength(80).IsRequired();
        builder.Property(x => x.ContentHmac).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ResultJson).HasMaxLength(2000).IsRequired();
        builder.HasIndex(x => new { x.ActorId, x.Operation, x.Key }).IsUnique();
        builder.HasOne<EmployeeAccount>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AttendanceRow>().WithMany().HasForeignKey(x => x.AttendanceId).OnDelete(DeleteBehavior.Restrict);
    }
}
