using Microsoft.AspNetCore.Identity;

namespace Estacionamento.Infrastructure.Identity;

public sealed class EmployeeAccount : IdentityUser<Guid>
{
    public bool IsActive { get; set; } = true;
}
