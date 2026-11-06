namespace Estacionamento.Infrastructure.Identity;

public static class EmployeeRoles
{
    public const string Valet = "manobrista";
    public const string Cashier = "caixa";
    public const string Administrator = "admin";

    public static readonly IReadOnlyList<string> All = [Valet, Cashier, Administrator];
}
