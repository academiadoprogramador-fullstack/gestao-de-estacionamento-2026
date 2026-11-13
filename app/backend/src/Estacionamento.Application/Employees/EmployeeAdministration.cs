using Estacionamento.Application.Abstractions;
using Estacionamento.Application.Security;

namespace Estacionamento.Application.Employees;

public sealed record EmployeeListItem(Guid Id, string Email, bool IsActive, IReadOnlyList<string> Roles, string ConcurrencyStamp);
public sealed record CreateEmployeeCommand(string Email, string InitialPassword, IReadOnlyCollection<string> Roles);
public sealed record UpdateEmployeeAccessCommand(Guid EmployeeId, string ConcurrencyStamp, bool IsActive, IReadOnlyCollection<string> Roles);
public enum EmployeeCreateStatus { Created, Invalid, DuplicateEmail, Forbidden }
public enum EmployeeListStatus { Listed, Invalid, Forbidden }
public enum EmployeeAccessStatus { Updated, Invalid, Forbidden, NotFound, Conflict, LastAdministrator }
public sealed record EmployeeCreateResult(EmployeeCreateStatus Status, EmployeeListItem? Employee = null);
public sealed record EmployeeAccessResult(EmployeeAccessStatus Status, EmployeeListItem? Employee = null, string? ConcurrencyStamp = null);

public sealed class EmployeeStoreOperationException : Exception
{
    public EmployeeStoreOperationException() : base("Employee creation could not be completed.") { }
}

public interface IEmployeeDirectory
{
    Task<(IReadOnlyList<EmployeeListItem> Items, int Total)> ListAsync(int page, int pageSize, CancellationToken cancellationToken);
    Task<EmployeeCreateResult> CreateAsync(CreateEmployeeCommand command, CancellationToken cancellationToken);
    Task<EmployeeAccessResult> UpdateAccessAsync(UpdateEmployeeAccessCommand command, CancellationToken cancellationToken);
}

public sealed class EmployeeAdministration(
    IEmployeeDirectory directory,
    ICurrentActor currentActor,
    IUseCaseAuthorizer authorizer,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    IClock clock)
{
    private static readonly HashSet<string> AllowedRoles = new(StringComparer.Ordinal)
    {
        EmployeeRoles.Valet, EmployeeRoles.Cashier, EmployeeRoles.Administrator
    };

    public async Task<(EmployeeListStatus Status, IReadOnlyList<EmployeeListItem> Items, int Total)> ListAsync(
        int page, int pageSize, CancellationToken cancellationToken)
    {
        if (!CanManageEmployees())
        {
            return (EmployeeListStatus.Forbidden, [], 0);
        }

        if (page < 1 || pageSize is < 1 or > 100)
        {
            return (EmployeeListStatus.Invalid, [], 0);
        }

        var result = await directory.ListAsync(page, pageSize, cancellationToken);
        return (EmployeeListStatus.Listed, result.Items, result.Total);
    }

    public async Task<EmployeeCreateResult> CreateAsync(CreateEmployeeCommand command, CancellationToken cancellationToken)
    {
        if (!CanManageEmployees())
        {
            return new EmployeeCreateResult(EmployeeCreateStatus.Forbidden);
        }

        if (command.Roles is null)
        {
            return new EmployeeCreateResult(EmployeeCreateStatus.Invalid);
        }

        var roles = command.Roles.Distinct(StringComparer.Ordinal).ToArray();
        if (string.IsNullOrWhiteSpace(command.Email)
            || string.IsNullOrWhiteSpace(command.InitialPassword)
            || roles.Length == 0
            || roles.Any(role => !AllowedRoles.Contains(role)))
        {
            return new EmployeeCreateResult(EmployeeCreateStatus.Invalid);
        }

        try
        {
            return await unitOfWork.ExecuteAsync(async token =>
            {
                var result = await directory.CreateAsync(command with { Roles = roles }, token);
                if (result.Status != EmployeeCreateStatus.Created || result.Employee is null)
                {
                    return result;
                }

                await auditWriter.AppendAsync(
                    currentActor.UserId!.Value,
                    "employee.created",
                    result.Employee.Id,
                    clock.UtcNow,
                    new Dictionary<string, string> { ["changedFields"] = "email,roles" },
                    token);
                return result;
            }, cancellationToken);
        }
        catch (EmployeeStoreOperationException)
        {
            return new EmployeeCreateResult(EmployeeCreateStatus.Invalid);
        }
    }

    public async Task<EmployeeAccessResult> UpdateAccessAsync(UpdateEmployeeAccessCommand command, CancellationToken cancellationToken)
    {
        if (!CanManageEmployees())
        {
            return new EmployeeAccessResult(EmployeeAccessStatus.Forbidden);
        }

        if (command.EmployeeId == Guid.Empty || string.IsNullOrWhiteSpace(command.ConcurrencyStamp) || command.Roles is null)
        {
            return new EmployeeAccessResult(EmployeeAccessStatus.Invalid);
        }

        var roles = command.Roles.Distinct(StringComparer.Ordinal).ToArray();
        if (roles.Length == 0 || roles.Any(role => !AllowedRoles.Contains(role)))
        {
            return new EmployeeAccessResult(EmployeeAccessStatus.Invalid);
        }

        try
        {
            return await unitOfWork.ExecuteAsync(async token =>
            {
                var result = await directory.UpdateAccessAsync(command with { Roles = roles }, token);
                if (result.Status != EmployeeAccessStatus.Updated || result.Employee is null)
                {
                    return result;
                }

                await auditWriter.AppendAsync(
                    currentActor.UserId!.Value,
                    "employee.access_updated",
                    result.Employee.Id,
                    clock.UtcNow,
                    new Dictionary<string, string> { ["changedFields"] = "roles,isActive" },
                    token);
                return result;
            }, cancellationToken);
        }
        catch (EmployeeStoreOperationException)
        {
            return new EmployeeAccessResult(EmployeeAccessStatus.Conflict);
        }
    }

    private bool CanManageEmployees() =>
        currentActor.UserId is not null
        && authorizer.IsAllowed(currentActor.Roles, EmployeeAction.ManageEmployees);
}
