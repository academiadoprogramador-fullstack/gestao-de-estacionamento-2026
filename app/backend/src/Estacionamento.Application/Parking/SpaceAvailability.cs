using Estacionamento.Application.Abstractions;
using Estacionamento.Application.Security;
using Estacionamento.Domain.Parking;

namespace Estacionamento.Application.Parking;

public sealed record ParkingSpaceView(Guid Id, string Code, bool IsOccupied, string Version);
public sealed record CreateParkingSpaceCommand(string Code);
public enum SpaceQueryStatus { Listed, Invalid, Forbidden }
public enum SpaceCreateStatus { Created, Invalid, Duplicate, Forbidden }
public sealed record SpaceCreateResult(SpaceCreateStatus Status, ParkingSpaceView? Space = null);

public interface ISpaceDirectory
{
    Task<(IReadOnlyList<ParkingSpaceView> Items, int Total)> ListAsync(string? code, bool? occupied, int page, int pageSize, CancellationToken cancellationToken);
    Task<SpaceCreateResult> CreateAsync(string normalizedCode, CancellationToken cancellationToken);
}

public sealed class SpaceAvailability(
    ISpaceDirectory directory,
    ICurrentActor actor,
    IUseCaseAuthorizer authorizer,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    IClock clock)
{
    public async Task<(SpaceQueryStatus Status, IReadOnlyList<ParkingSpaceView> Items, int Total)> ListAsync(
        string? code, string? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (!Allowed(EmployeeAction.ReadSpaces))
        {
            return (SpaceQueryStatus.Forbidden, [], 0);
        }

        if (page < 1 || pageSize is < 1 or > 100
            || (status is not null && status is not ("free" or "occupied")))
        {
            return (SpaceQueryStatus.Invalid, [], 0);
        }

        string? normalizedCode = null;
        if (!string.IsNullOrWhiteSpace(code))
        {
            try { normalizedCode = SpaceCode.Create(code).Value; }
            catch (ArgumentException) { return (SpaceQueryStatus.Invalid, [], 0); }
        }

        var result = await directory.ListAsync(normalizedCode, status is null ? null : status == "occupied", page, pageSize, cancellationToken);
        return (SpaceQueryStatus.Listed, result.Items, result.Total);
    }

    public async Task<SpaceCreateResult> CreateAsync(CreateParkingSpaceCommand command, CancellationToken cancellationToken)
    {
        if (!Allowed(EmployeeAction.ManageSpaces))
        {
            return new SpaceCreateResult(SpaceCreateStatus.Forbidden);
        }

        SpaceCode code;
        try { code = SpaceCode.Create(command.Code); }
        catch (ArgumentException) { return new SpaceCreateResult(SpaceCreateStatus.Invalid); }

        return await unitOfWork.ExecuteAsync(async token =>
        {
            var result = await directory.CreateAsync(code.Value, token);
            if (result.Status != SpaceCreateStatus.Created || result.Space is null)
            {
                return result;
            }

            await auditWriter.AppendAsync(actor.UserId!.Value, "space.created", result.Space.Id, clock.UtcNow,
                new Dictionary<string, string> { ["spaceId"] = result.Space.Id.ToString("D") }, token);
            return result;
        }, cancellationToken);
    }

    private bool Allowed(EmployeeAction action) => actor.UserId is not null && authorizer.IsAllowed(actor.Roles, action);
}
