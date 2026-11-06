using Estacionamento.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Estacionamento.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class SyntheticAccountProvisioningTests
{
    [TestMethod]
    public async Task Provisioning_is_idempotent_and_assigns_each_synthetic_role()
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_SQLSERVER_CONNECTION")
            ?? throw new InvalidOperationException("Set TEST_SQLSERVER_CONNECTION for SQL Server integration tests.");
        var passwords = new Dictionary<string, string?>
        {
            ["SyntheticAccounts:ValetPassword"] = Environment.GetEnvironmentVariable("TEST_VALET_PASSWORD"),
            ["SyntheticAccounts:CashierPassword"] = Environment.GetEnvironmentVariable("TEST_CASHIER_PASSWORD"),
            ["SyntheticAccounts:AdministratorPassword"] = Environment.GetEnvironmentVariable("TEST_ADMIN_PASSWORD")
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(passwords).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<IdentityDbContext>(options => options.UseSqlServer(connectionString));
        services.AddIdentityCore<EmployeeAccount>()
            .AddRoles<EmployeeRole>()
            .AddEntityFrameworkStores<IdentityDbContext>();
        services.AddScoped<SyntheticAccountProvisioner>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.MigrateAsync();
        var provisioner = scope.ServiceProvider.GetRequiredService<SyntheticAccountProvisioner>();
        await provisioner.ProvisionAsync(configuration);
        await provisioner.ProvisionAsync(configuration);

        var users = scope.ServiceProvider.GetRequiredService<UserManager<EmployeeAccount>>();
        foreach (var role in EmployeeRoles.All)
        {
            Assert.IsTrue(await scope.ServiceProvider.GetRequiredService<RoleManager<EmployeeRole>>()
                .RoleExistsAsync(role));
            var account = await users.FindByEmailAsync(role switch
            {
                EmployeeRoles.Valet => "manobrista@local.example.invalid",
                EmployeeRoles.Cashier => "caixa@local.example.invalid",
                EmployeeRoles.Administrator => "admin@local.example.invalid",
                _ => throw new ArgumentOutOfRangeException(nameof(role))
            });
            Assert.IsNotNull(account);
            Assert.IsTrue(await users.IsInRoleAsync(account, role));
        }

        Assert.AreEqual(3, await users.Users.CountAsync(user => user.Email!.EndsWith("@local.example.invalid")));
    }
}
