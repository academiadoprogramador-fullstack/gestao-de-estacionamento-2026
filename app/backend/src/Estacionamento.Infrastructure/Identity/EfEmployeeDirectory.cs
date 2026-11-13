using Estacionamento.Application.Employees;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Estacionamento.Infrastructure.Identity;

public sealed class EfEmployeeDirectory(UserManager<EmployeeAccount> userManager, IdentityDbContext dbContext) : IEmployeeDirectory
{
    public async Task<(IReadOnlyList<EmployeeListItem> Items, int Total)> ListAsync(
        int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = userManager.Users.AsNoTracking().OrderBy(user => user.Email).ThenBy(user => user.Id);
        var total = await query.CountAsync(cancellationToken);
        var users = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var items = new List<EmployeeListItem>(users.Count);
        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            items.Add(new EmployeeListItem(user.Id, user.Email ?? string.Empty, user.IsActive, roles.Order(StringComparer.Ordinal).ToArray(), user.ConcurrencyStamp ?? string.Empty));
        }

        return (items, total);
    }

    public async Task<EmployeeCreateResult> CreateAsync(CreateEmployeeCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (await userManager.FindByEmailAsync(command.Email) is not null)
        {
            return new EmployeeCreateResult(EmployeeCreateStatus.DuplicateEmail);
        }

        var user = new EmployeeAccount
        {
            UserName = command.Email.Trim(),
            Email = command.Email.Trim(),
            IsActive = true,
            LockoutEnabled = true
        };

        IdentityResult createResult;
        try
        {
            createResult = await userManager.CreateAsync(user, command.InitialPassword);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            return new EmployeeCreateResult(EmployeeCreateStatus.DuplicateEmail);
        }

        if (!createResult.Succeeded)
        {
            return new EmployeeCreateResult(createResult.Errors.Any(error => error.Code.Contains("DuplicateEmail", StringComparison.Ordinal))
                ? EmployeeCreateStatus.DuplicateEmail
                : EmployeeCreateStatus.Invalid);
        }

        var roleResult = await userManager.AddToRolesAsync(user, command.Roles);
        if (!roleResult.Succeeded)
        {
            throw new EmployeeStoreOperationException();
        }

        return new EmployeeCreateResult(
            EmployeeCreateStatus.Created,
            new EmployeeListItem(user.Id, user.Email!, user.IsActive, command.Roles.Order(StringComparer.Ordinal).ToArray(), user.ConcurrencyStamp ?? string.Empty));
    }

    public async Task<EmployeeAccessResult> UpdateAccessAsync(UpdateEmployeeAccessCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE [AccessAdministrationControl] WITH (UPDLOCK, ROWLOCK) SET [Id] = [Id] WHERE [Id] = 1",
            cancellationToken);

        var user = await userManager.FindByIdAsync(command.EmployeeId.ToString());
        if (user is null)
        {
            return new EmployeeAccessResult(EmployeeAccessStatus.NotFound);
        }

        if (!string.Equals(user.ConcurrencyStamp, command.ConcurrencyStamp, StringComparison.Ordinal))
        {
            return new EmployeeAccessResult(EmployeeAccessStatus.Conflict);
        }

        var currentRoles = await userManager.GetRolesAsync(user);
        var remainsAdministrator = command.IsActive && command.Roles.Contains(EmployeeRoles.Administrator, StringComparer.Ordinal);
        var removesAdministrator = currentRoles.Contains(EmployeeRoles.Administrator, StringComparer.Ordinal) && !remainsAdministrator;
        if (user.IsActive && removesAdministrator)
        {
            var activeAdministrators = await userManager.GetUsersInRoleAsync(EmployeeRoles.Administrator);
            if (activeAdministrators.Count(account => account.IsActive) <= 1)
            {
                return new EmployeeAccessResult(EmployeeAccessStatus.LastAdministrator);
            }
        }

        var removed = await userManager.RemoveFromRolesAsync(user, currentRoles);
        if (!removed.Succeeded)
        {
            throw new EmployeeStoreOperationException();
        }

        var added = await userManager.AddToRolesAsync(user, command.Roles);
        if (!added.Succeeded)
        {
            throw new EmployeeStoreOperationException();
        }

        user.IsActive = command.IsActive;
        var stampResult = await userManager.UpdateSecurityStampAsync(user);
        if (!stampResult.Succeeded)
        {
            throw new EmployeeStoreOperationException();
        }

        var update = await userManager.UpdateAsync(user);
        if (!update.Succeeded)
        {
            throw new EmployeeStoreOperationException();
        }

        return new EmployeeAccessResult(
            EmployeeAccessStatus.Updated,
            new EmployeeListItem(user.Id, user.Email ?? string.Empty, user.IsActive, command.Roles.Order(StringComparer.Ordinal).ToArray(), user.ConcurrencyStamp ?? string.Empty),
            user.ConcurrencyStamp);
    }

    private static bool IsUniqueConstraintViolation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is SqlException { Number: 2601 or 2627 })
            {
                return true;
            }
        }

        return false;
    }
}
