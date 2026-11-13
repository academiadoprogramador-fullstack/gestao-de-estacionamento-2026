namespace Estacionamento.Domain.Parking;

public sealed record Occupancy
{
    private Occupancy(Guid id, Guid spaceId, Guid attendanceId, DateTimeOffset startedAtUtc)
    {
        Id = id;
        SpaceId = spaceId;
        AttendanceId = attendanceId;
        StartedAtUtc = startedAtUtc;
    }

    public Guid Id { get; }
    public Guid SpaceId { get; }
    public Guid AttendanceId { get; }
    public DateTimeOffset StartedAtUtc { get; }
    public DateTimeOffset? EndedAtUtc { get; private init; }
    public bool IsOpen => EndedAtUtc is null;

    public static Occupancy Open(Guid spaceId, Guid attendanceId, DateTimeOffset startedAtUtc)
    {
        if (spaceId == Guid.Empty || attendanceId == Guid.Empty)
        {
            throw new ArgumentException("Space and attendance identifiers are required.");
        }

        return new Occupancy(Guid.NewGuid(), spaceId, attendanceId, startedAtUtc.ToUniversalTime());
    }

    public Occupancy Close(DateTimeOffset endedAtUtc)
    {
        if (!IsOpen || endedAtUtc.ToUniversalTime() < StartedAtUtc)
        {
            throw new InvalidOperationException("An open occupancy can only be closed at or after its start time.");
        }

        return this with { EndedAtUtc = endedAtUtc.ToUniversalTime() };
    }
}
