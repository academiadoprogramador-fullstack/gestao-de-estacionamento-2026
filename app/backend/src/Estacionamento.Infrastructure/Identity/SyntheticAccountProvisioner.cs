using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Estacionamento.Infrastructure.Identity;

public sealed class SyntheticAccountProvisioner(
    RoleManager<EmployeeRole> roleManager,
    UserManager<EmployeeAccount> userManager,
    ILogger<SyntheticAccountProvisioner> logger)
{
    private static readonly (string Role, string Email, string PasswordKey)[] Accounts =
    [
        (EmployeeRoles.Valet, "manobrista@local.example.invalid", "SyntheticAccounts:ValetPassword"),
        (EmployeeRoles.Cashier, "caixa@local.example.invalid", "SyntheticAccounts:CashierPassword"),
        (EmployeeRoles.Administrator, "admin@local.example.invalid", "SyntheticAccounts:AdministratorPassword")
    ];

    public async Task ProvisionAsync(IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        foreach (var roleName in EmployeeRoles.All)
        {
            if (await roleManager.RoleExistsAsync(roleName))
            {
                continue;
            }

            var roleResult = await roleManager.CreateAsync(new EmployeeRole { Name = roleName });
            EnsureSucceeded(roleResult, $"creating role '{roleName}'");
        }

        foreach (var account in Accounts)
        {
            var password = configuration[account.PasswordKey];
            if (string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException(
                    $"Required configuration '{account.PasswordKey}' is missing for synthetic account provisioning.");
            }

            var user = await userManager.FindByEmailAsync(account.Email);
            if (user is null)
            {
                user = new EmployeeAccount
                {
                    UserName = account.Email,
                    Email = account.Email,
                    EmailConfirmed = true,
                    LockoutEnabled = true
                };
                var createResult = await userManager.CreateAsync(user, password);
                EnsureSucceeded(createResult, $"creating synthetic account for role '{account.Role}'");
                logger.LogInformation("Provisioned synthetic employee account for role {Role}.", account.Role);
            }

            if (!await userManager.IsInRoleAsync(user, account.Role))
            {
                var addRoleResult = await userManager.AddToRoleAsync(user, account.Role);
                EnsureSucceeded(addRoleResult, $"assigning role '{account.Role}' to synthetic account");
            }
        }
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
        {
            var codes = string.Join(", ", result.Errors.Select(error => error.Code));
            throw new InvalidOperationException($"Identity failed while {operation}. Error codes: {codes}");
        }
    }
}
