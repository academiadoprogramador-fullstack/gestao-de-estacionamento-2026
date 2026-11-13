namespace Estacionamento.Application.Abstractions;

public interface ICurrentActor
{
    Guid? UserId { get; }
    IReadOnlyCollection<string> Roles { get; }
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IUnitOfWork
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken);
}

public interface IFieldProtector
{
    string Protect(string value, Guid aggregateId, string field, int version);
    string Unprotect(string protectedValue, Guid aggregateId, string field, int version);
}

public interface IAuditWriter
{
    Task AppendAsync(Guid actorId, string action, Guid? targetId, DateTimeOffset occurredAtUtc, IReadOnlyDictionary<string, string> metadata, CancellationToken cancellationToken);
}

public sealed record AuditEventView(Guid ActorId, string Action, Guid? TargetId, Guid? AttendanceId, DateTimeOffset OccurredAtUtc, string MetadataJson);

public interface IAuditReader
{
    Task<(IReadOnlyList<AuditEventView> Items, int Total)> ReadAttendanceHistoryAsync(
        Guid attendanceId, int page, int pageSize, bool includeFinancialEvents, CancellationToken cancellationToken);
}
