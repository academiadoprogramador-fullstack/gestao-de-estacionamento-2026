namespace Estacionamento.Application.Security;

public interface IUseCaseAuthorizer
{
    bool IsAllowed(IReadOnlyCollection<string> roles, EmployeeAction action);
}

public sealed class RoleUseCaseAuthorizer : IUseCaseAuthorizer
{
    private static readonly IReadOnlyDictionary<EmployeeAction, string[]> AllowedRoles = new Dictionary<EmployeeAction, string[]>
    {
        [EmployeeAction.ListEmployees] = [EmployeeRoles.Administrator],
        [EmployeeAction.ManageEmployees] = [EmployeeRoles.Administrator],
        [EmployeeAction.ManageSpaces] = [EmployeeRoles.Administrator],
        [EmployeeAction.ReadSpaces] = [EmployeeRoles.Valet, EmployeeRoles.Cashier, EmployeeRoles.Administrator],
        [EmployeeAction.ManageDailyRates] = [EmployeeRoles.Administrator],
        [EmployeeAction.CreateAttendance] = [EmployeeRoles.Cashier, EmployeeRoles.Administrator],
        [EmployeeAction.ReadAttendances] = [EmployeeRoles.Valet, EmployeeRoles.Cashier, EmployeeRoles.Administrator],
        [EmployeeAction.ReadFinancialDetails] = [EmployeeRoles.Cashier, EmployeeRoles.Administrator],
        [EmployeeAction.ReadDocument] = [EmployeeRoles.Cashier, EmployeeRoles.Administrator],
        [EmployeeAction.ReadEntryInspection] = [EmployeeRoles.Valet, EmployeeRoles.Cashier, EmployeeRoles.Administrator]
    };

    public bool IsAllowed(IReadOnlyCollection<string> roles, EmployeeAction action) =>
        AllowedRoles.TryGetValue(action, out var allowed) && roles.Any(role => allowed.Contains(role, StringComparer.Ordinal));
}
