using Estacionamento.Infrastructure.Identity;

namespace Estacionamento.Infrastructure.Persistence;

public sealed class ParkingSpaceRow
{
    public Guid Id { get; set; }
    public required string Code { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class DailyRateRow
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset PublishedAtUtc { get; set; }
    public Guid PublishedById { get; set; }
}

public sealed class DailyRateConfigurationRow
{
    public int Id { get; set; }
    public Guid? CurrentRateId { get; set; }
    public DailyRateRow? CurrentRate { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class AccessAdministrationControlRow
{
    public int Id { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class AttendanceRow
{
    public Guid Id { get; set; }
    public required string PublicIdentifier { get; set; }
    public required string NormalizedPlate { get; set; }
    public required string Model { get; set; }
    public required string OwnerName { get; set; }
    public required string EncryptedDocument { get; set; }
    public Guid DailyRateId { get; set; }
    public decimal DailyRateSnapshot { get; set; }
    public DateTimeOffset EnteredAtUtc { get; set; }
    public Guid OperatorId { get; set; }
    public required string State { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class EntryInspectionRow
{
    public Guid Id { get; set; }
    public Guid AttendanceId { get; set; }
    public required string EncryptedText { get; set; }
    public Guid ActorId { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
}

public sealed class OccupancyRow
{
    public Guid Id { get; set; }
    public Guid SpaceId { get; set; }
    public Guid AttendanceId { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? EndedAtUtc { get; set; }
    public Guid ActorId { get; set; }
}

public sealed class AuditEventRow
{
    public Guid Id { get; set; }
    public Guid ActorId { get; set; }
    public required string Action { get; set; }
    public Guid? TargetId { get; set; }
    public Guid? AttendanceId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public required string MetadataJson { get; set; }
}

public sealed class IdempotentResultRow
{
    public Guid Id { get; set; }
    public Guid ActorId { get; set; }
    public required string Operation { get; set; }
    public Guid Key { get; set; }
    public required byte[] ContentHmac { get; set; }
    public int AlgorithmVersion { get; set; }
    public required string ResultJson { get; set; }
    public Guid? AttendanceId { get; set; }
}
