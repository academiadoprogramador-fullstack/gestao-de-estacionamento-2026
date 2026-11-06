using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Estacionamento.Infrastructure.Identity;

public sealed class IdentityDesignTimeFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Identity")
            ?? throw new InvalidOperationException(
                "ConnectionStrings__Identity must be set to generate or apply Identity migrations.");

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new IdentityDbContext(options);
    }
}
