using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Estacionamento.Infrastructure.Identity;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : IdentityDbContext<EmployeeAccount, EmployeeRole, Guid>(options)
{
}
