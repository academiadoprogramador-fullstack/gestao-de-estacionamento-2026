using Estacionamento.Infrastructure.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Estacionamento.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class OperationalMigrationTests
{
    [TestMethod]
    public async Task Latest_migrations_create_operational_schema_without_commercial_seed()
    {
        var connection = CreateIsolatedConnection();
        try
        {
            await using var db = CreateContext(connection);
            await db.Database.MigrateAsync();

            var tables = await db.Database.SqlQueryRaw<string>(
                    "SELECT TABLE_NAME AS [Value] FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'")
                .ToListAsync();
            CollectionAssert.IsSubsetOf(
                new[] { "ParkingSpaces", "DailyRates", "DailyRateConfiguration", "AccessAdministrationControl", "Attendances", "Occupancies", "EntryInspections", "AuditEvents", "IdempotentResults" },
                tables.ToArray());
            Assert.AreEqual(0, await db.ParkingSpaces.CountAsync());
            Assert.AreEqual(0, await db.DailyRates.CountAsync());
            Assert.AreEqual(1, await db.DailyRateConfigurations.CountAsync());
            Assert.AreEqual(1, await db.AccessAdministrationControls.CountAsync());
        }
        finally
        {
            await DropDatabaseAsync(connection);
        }
    }

    [TestMethod]
    public async Task Operational_migration_preserves_accounts_from_identity_schema()
    {
        var connection = CreateIsolatedConnection();
        var userId = Guid.NewGuid();
        try
        {
            await using (var db = CreateContext(connection))
            {
                await db.Database.MigrateAsync("20261106180309_AddEmployeeActiveStatus");
                db.Users.Add(new EmployeeAccount
                {
                    Id = userId,
                    UserName = "preserved@example.test",
                    NormalizedUserName = "PRESERVED@EXAMPLE.TEST",
                    Email = "preserved@example.test",
                    NormalizedEmail = "PRESERVED@EXAMPLE.TEST",
                    IsActive = true
                });
                await db.SaveChangesAsync();
            }

            await using (var upgraded = CreateContext(connection))
            {
                await upgraded.Database.MigrateAsync();
                var account = await upgraded.Users.SingleAsync(user => user.Id == userId);
                Assert.AreEqual("preserved@example.test", account.Email);
                Assert.IsTrue(account.IsActive);
                Assert.AreEqual(0, await upgraded.DailyRates.CountAsync());
            }
        }
        finally
        {
            await DropDatabaseAsync(connection);
        }
    }

    [TestMethod]
    public async Task Unique_email_migration_reports_existing_duplicates_without_deleting_accounts()
    {
        var connection = CreateIsolatedConnection();
        try
        {
            await using (var historical = CreateContext(connection))
            {
                await historical.Database.MigrateAsync("20261106180309_AddEmployeeActiveStatus");
                historical.Users.AddRange(
                    User("duplicate-1", "duplicate@example.test"),
                    User("duplicate-2", "DUPLICATE@example.test"));
                await historical.SaveChangesAsync();
            }

            await using var upgraded = CreateContext(connection);
            var exception = await Assert.ThrowsExactlyAsync<SqlException>(() => upgraded.Database.MigrateAsync());
            Assert.AreEqual(51000, exception.Number);
            Assert.AreEqual(2, await upgraded.Users.CountAsync());
            var applied = await upgraded.Database.GetAppliedMigrationsAsync();
            Assert.IsFalse(applied.Any(migration => migration.EndsWith("_AddOperationalModules", StringComparison.Ordinal)));
        }
        finally
        {
            await DropDatabaseAsync(connection);
        }
    }

    private static EmployeeAccount User(string name, string email) => new()
    {
        Id = Guid.NewGuid(),
        UserName = name,
        NormalizedUserName = name.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = "DUPLICATE@EXAMPLE.TEST",
        IsActive = true
    };

    private static SqlConnectionStringBuilder CreateIsolatedConnection()
    {
        var configured = Environment.GetEnvironmentVariable("TEST_SQLSERVER_CONNECTION")
            ?? throw new InvalidOperationException("Set TEST_SQLSERVER_CONNECTION to an isolated SQL Server test server.");
        return new SqlConnectionStringBuilder(configured) { InitialCatalog = $"EstacionamentoMigration_{Guid.NewGuid():N}" };
    }

    private static IdentityDbContext CreateContext(SqlConnectionStringBuilder connection) =>
        new(new DbContextOptionsBuilder<IdentityDbContext>().UseSqlServer(connection.ConnectionString).Options);

    private static async Task DropDatabaseAsync(SqlConnectionStringBuilder connection)
    {
        await using var admin = new SqlConnection(new SqlConnectionStringBuilder(connection.ConnectionString) { InitialCatalog = "master" }.ConnectionString);
        await admin.OpenAsync();
        await using var command = admin.CreateCommand();
        command.CommandText = $"IF DB_ID(@databaseName) IS NOT NULL BEGIN ALTER DATABASE [{connection.InitialCatalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{connection.InitialCatalog}]; END";
        command.Parameters.AddWithValue("@databaseName", connection.InitialCatalog);
        await command.ExecuteNonQueryAsync();
    }
}
