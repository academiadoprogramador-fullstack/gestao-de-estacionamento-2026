using System.Text.Json;
using Estacionamento.Application.Abstractions;
using Estacionamento.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace Estacionamento.Infrastructure.Persistence;

public sealed class EfAuditStore(IdentityDbContext dbContext) : IAuditWriter, IAuditReader
{
    private static readonly HashSet<string> AllowedMetadataKeys = new(StringComparer.Ordinal)
    {
        "spaceId", "dailyRateId", "attendanceId", "version", "changedFields"
    };

    public async Task AppendAsync(
        Guid actorId,
        string action,
        Guid? targetId,
        DateTimeOffset occurredAtUtc,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        if (actorId == Guid.Empty || string.IsNullOrWhiteSpace(action) || action.Length > 80)
        {
            throw new ArgumentException("A valid actor and bounded audit action are required.");
        }

        if (metadata.Keys.Any(key => !AllowedMetadataKeys.Contains(key)))
        {
            throw new ArgumentException("Audit metadata contains a key that is not allowlisted.", nameof(metadata));
        }

        var safeMetadata = metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        dbContext.AuditEvents.Add(new AuditEventRow
        {
            Id = Guid.NewGuid(),
            ActorId = actorId,
            Action = action,
            TargetId = targetId,
            AttendanceId = metadata.TryGetValue("attendanceId", out var id) && Guid.TryParse(id, out var attendanceId) ? attendanceId : null,
            OccurredAtUtc = occurredAtUtc.ToUniversalTime(),
            MetadataJson = JsonSerializer.Serialize(safeMetadata)
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<AuditEventView> Items, int Total)> ReadAttendanceHistoryAsync(
        Guid attendanceId,
        int page,
        int pageSize,
        bool includeFinancialEvents,
        CancellationToken cancellationToken)
    {
        if (attendanceId == Guid.Empty || page < 1 || pageSize is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(page), "Attendance and valid pagination are required.");
        }

        var query = dbContext.AuditEvents.AsNoTracking()
            .Where(item => item.AttendanceId == attendanceId);
        if (!includeFinancialEvents)
        {
            query = query.Where(item => item.Action != "daily_rate.captured");
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(item => item.OccurredAtUtc)
            .ThenBy(item => item.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(item => new AuditEventView(item.ActorId, item.Action, item.TargetId, item.AttendanceId, item.OccurredAtUtc, item.MetadataJson))
            .ToListAsync(cancellationToken);
        return (items, total);
    }
}
