using Estacionamento.Application.Abstractions;
using Estacionamento.Application.Security;
using Estacionamento.Domain.Parking;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Estacionamento.Application.Parking;

public sealed record AttendanceSearchItem(
    Guid Id,
    string PublicIdentifier,
    string Plate,
    string Model,
    DateTimeOffset EnteredAtUtc,
    string State,
    Guid ParkingSpaceId,
    string ParkingSpaceCode);

public sealed record AttendanceSearchPage(
    IReadOnlyList<AttendanceSearchItem> Items,
    int Page,
    int PageSize,
    int Total);

public enum AttendanceSearchStatus { Found, Invalid, Forbidden }
public sealed record AttendanceSearchOutcome(AttendanceSearchStatus Status, AttendanceSearchPage? Page = null);

public interface IAttendanceDirectory
{
    Task<(IReadOnlyList<AttendanceSearchItem> Items, int Total)> SearchAsync(
        string? normalizedPlate, string? normalizedIdentifier, int page, int pageSize,
        CancellationToken cancellationToken);
    Task<AttendanceDetailData?> GetDetailAsync(Guid attendanceId, CancellationToken cancellationToken);
    Task<string?> GetEncryptedDocumentAsync(Guid attendanceId, CancellationToken cancellationToken);
    Task<string?> GetEncryptedEntryInspectionAsync(Guid attendanceId, CancellationToken cancellationToken);
}

public sealed record AttendanceDetailData(
    Guid Id,
    string PublicIdentifier,
    string Plate,
    string Model,
    string OwnerName,
    DateTimeOffset EnteredAtUtc,
    string State,
    Guid OperatorId,
    Guid ParkingSpaceId,
    string ParkingSpaceCode,
    Guid DailyRateId,
    decimal DailyRateSnapshot);

public sealed record AttendanceDetailView(
    Guid Id,
    string PublicIdentifier,
    string Plate,
    string Model,
    string OwnerName,
    DateTimeOffset EnteredAtUtc,
    string State,
    Guid OperatorId,
    Guid ParkingSpaceId,
    string ParkingSpaceCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? DailyRateId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? DailyRateSnapshot);

public sealed record AttendanceHistoryEvent(
    Guid ActorId,
    string Action,
    Guid? TargetId,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record AttendanceHistoryPage(
    IReadOnlyList<AttendanceHistoryEvent> Items,
    int Page,
    int PageSize,
    int Total);

public enum AttendanceDetailStatus { Found, NotFound, Invalid, Forbidden }
public sealed record AttendanceDetailOutcome(AttendanceDetailStatus Status, AttendanceDetailView? Detail = null);
public sealed record AttendanceHistoryOutcome(AttendanceDetailStatus Status, AttendanceHistoryPage? History = null);
public enum SensitiveFieldStatus { Read, NotFound, Invalid, Forbidden }
public sealed record SensitiveFieldOutcome(SensitiveFieldStatus Status, string? Value = null);

public sealed class AttendanceQuery(
    IAttendanceDirectory directory,
    ICurrentActor actor,
    IUseCaseAuthorizer authorizer,
    IAuditReader auditReader,
    IFieldProtector fieldProtector,
    IAuditWriter auditWriter,
    IClock clock)
{
    public async Task<AttendanceSearchOutcome> SearchAsync(
        string? plate,
        string? identifier,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (actor.UserId is null || !authorizer.IsAllowed(actor.Roles, EmployeeAction.ReadAttendances))
        {
            return new AttendanceSearchOutcome(AttendanceSearchStatus.Forbidden);
        }

        if (page < 1 || pageSize is < 1 or > 100 || (plate is null) == (identifier is null))
        {
            return new AttendanceSearchOutcome(AttendanceSearchStatus.Invalid);
        }

        string normalizedPlate;
        string normalizedIdentifier;
        try
        {
            normalizedPlate = plate is null ? string.Empty : VehiclePlate.Create(plate).Value;
            normalizedIdentifier = identifier is null ? string.Empty : NormalizeIdentifier(identifier);
        }
        catch (ArgumentException)
        {
            return new AttendanceSearchOutcome(AttendanceSearchStatus.Invalid);
        }

        if (identifier is not null && normalizedIdentifier.Length == 0)
        {
            return new AttendanceSearchOutcome(AttendanceSearchStatus.Invalid);
        }

        var (items, total) = await directory.SearchAsync(
            plate is null ? null : normalizedPlate,
            identifier is null ? null : normalizedIdentifier,
            page,
            pageSize,
            cancellationToken);
        return new AttendanceSearchOutcome(
            AttendanceSearchStatus.Found,
            new AttendanceSearchPage(items, page, pageSize, total));
    }

    public async Task<AttendanceDetailOutcome> GetDetailAsync(Guid attendanceId, CancellationToken cancellationToken)
    {
        if (actor.UserId is null || !authorizer.IsAllowed(actor.Roles, EmployeeAction.ReadAttendances))
        {
            return new AttendanceDetailOutcome(AttendanceDetailStatus.Forbidden);
        }

        if (attendanceId == Guid.Empty)
        {
            return new AttendanceDetailOutcome(AttendanceDetailStatus.Invalid);
        }

        var detail = await directory.GetDetailAsync(attendanceId, cancellationToken);
        if (detail is null)
        {
            return new AttendanceDetailOutcome(AttendanceDetailStatus.NotFound);
        }

        var canReadFinancial = authorizer.IsAllowed(actor.Roles, EmployeeAction.ReadFinancialDetails);
        return new AttendanceDetailOutcome(AttendanceDetailStatus.Found, new AttendanceDetailView(
            detail.Id,
            detail.PublicIdentifier,
            detail.Plate,
            detail.Model,
            detail.OwnerName,
            detail.EnteredAtUtc,
            detail.State,
            detail.OperatorId,
            detail.ParkingSpaceId,
            detail.ParkingSpaceCode,
            canReadFinancial ? detail.DailyRateId : null,
            canReadFinancial ? detail.DailyRateSnapshot : null));
    }

    public async Task<AttendanceHistoryOutcome> GetHistoryAsync(
        Guid attendanceId, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (actor.UserId is null || !authorizer.IsAllowed(actor.Roles, EmployeeAction.ReadAttendances))
        {
            return new AttendanceHistoryOutcome(AttendanceDetailStatus.Forbidden);
        }

        if (attendanceId == Guid.Empty || page < 1 || pageSize is < 1 or > 100)
        {
            return new AttendanceHistoryOutcome(AttendanceDetailStatus.Invalid);
        }

        if (await directory.GetDetailAsync(attendanceId, cancellationToken) is null)
        {
            return new AttendanceHistoryOutcome(AttendanceDetailStatus.NotFound);
        }

        var includeFinancialEvents = authorizer.IsAllowed(actor.Roles, EmployeeAction.ReadFinancialDetails);
        var (events, total) = await auditReader.ReadAttendanceHistoryAsync(
            attendanceId, page, pageSize, includeFinancialEvents, cancellationToken);
        var items = events.Select(item => new AttendanceHistoryEvent(
            item.ActorId,
            item.Action,
            item.TargetId,
            item.OccurredAtUtc,
            ProjectMetadata(item.MetadataJson, includeFinancialEvents))).ToArray();
        return new AttendanceHistoryOutcome(
            AttendanceDetailStatus.Found,
            new AttendanceHistoryPage(items, page, pageSize, total));
    }

    public Task<SensitiveFieldOutcome> ReadDocumentAsync(Guid attendanceId, CancellationToken cancellationToken) =>
        ReadSensitiveAsync(attendanceId, "document", EmployeeAction.ReadDocument,
            directory.GetEncryptedDocumentAsync, cancellationToken);

    public Task<SensitiveFieldOutcome> ReadEntryInspectionAsync(Guid attendanceId, CancellationToken cancellationToken) =>
        ReadSensitiveAsync(attendanceId, "entry-inspection", EmployeeAction.ReadEntryInspection,
            directory.GetEncryptedEntryInspectionAsync, cancellationToken);

    private async Task<SensitiveFieldOutcome> ReadSensitiveAsync(
        Guid attendanceId,
        string field,
        EmployeeAction action,
        Func<Guid, CancellationToken, Task<string?>> readEncrypted,
        CancellationToken cancellationToken)
    {
        if (actor.UserId is not Guid actorId || !authorizer.IsAllowed(actor.Roles, action))
        {
            return new SensitiveFieldOutcome(SensitiveFieldStatus.Forbidden);
        }

        if (attendanceId == Guid.Empty)
        {
            return new SensitiveFieldOutcome(SensitiveFieldStatus.Invalid);
        }

        var encrypted = await readEncrypted(attendanceId, cancellationToken);
        if (encrypted is null)
        {
            return new SensitiveFieldOutcome(SensitiveFieldStatus.NotFound);
        }

        var plaintext = fieldProtector.Unprotect(encrypted, attendanceId, field, 1);
        await auditWriter.AppendAsync(actorId, $"attendance.{field}.read", attendanceId, clock.UtcNow.ToUniversalTime(),
            new Dictionary<string, string> { ["attendanceId"] = attendanceId.ToString("D") }, cancellationToken);
        return new SensitiveFieldOutcome(SensitiveFieldStatus.Read, plaintext);
    }

    private static IReadOnlyDictionary<string, string> ProjectMetadata(string metadataJson, bool includeFinancialEvents)
    {
        var metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson) ?? [];
        if (!includeFinancialEvents)
        {
            metadata.Remove("dailyRateId");
        }

        return metadata;
    }

    private static string NormalizeIdentifier(string value) =>
        Guid.TryParseExact(value.Trim(), "N", out var parsed) && parsed != Guid.Empty
            ? parsed.ToString("N").ToUpperInvariant()
            : string.Empty;
}
