using Estacionamento.Infrastructure.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Estacionamento.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class DevelopmentBootstrapTests
{
    [TestMethod]
    public async Task Development_startup_migrates_and_provisions_once_across_restarts()
    {
        var connectionString = NewTestDatabaseConnection();
        try
        {
            for (var restart = 0; restart < 2; restart++)
            {
                using var factory = new TestApplicationFactory("Development", connectionString);
                using var client = factory.CreateClient();
                using var response = await client.GetAsync("/healthz");
                Assert.IsTrue(response.IsSuccessStatusCode);

                await using var scope = factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                Assert.AreEqual(3, await db.Users.CountAsync(user => user.Email!.EndsWith("@local.example.invalid")));
                Assert.AreEqual(3, await db.Roles.CountAsync());
                Assert.IsFalse((await db.Database.GetPendingMigrationsAsync()).Any());
            }
        }
        finally
        {
            await DeleteTestDatabaseAsync(connectionString);
        }
    }

    [TestMethod]
    public async Task Non_development_startup_does_not_create_database()
    {
        var connectionString = NewTestDatabaseConnection();
        using var factory = new TestApplicationFactory("Testing", connectionString);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/healthz");
        Assert.IsTrue(response.IsSuccessStatusCode);
        Assert.IsFalse(await DatabaseExistsAsync(connectionString));
    }

    [TestMethod]
    public async Task Development_startup_rejects_missing_synthetic_password()
    {
        var connectionString = NewTestDatabaseConnection();
        try
        {
            using var factory = new TestApplicationFactory("Development", connectionString, omitValetPassword: true);
            var error = Assert.ThrowsExactly<InvalidOperationException>(() => factory.CreateClient());
            StringAssert.Contains(error.Message, "SyntheticAccounts:ValetPassword");
        }
        finally
        {
            await DeleteTestDatabaseAsync(connectionString);
        }
    }

    private static string NewTestDatabaseConnection()
    {
        var configured = Environment.GetEnvironmentVariable("TEST_SQLSERVER_CONNECTION")
            ?? throw new InvalidOperationException("Set TEST_SQLSERVER_CONNECTION to an isolated SQL Server test instance.");
        var connection = new SqlConnectionStringBuilder(configured)
        {
            InitialCatalog = $"EstacionamentoBootstrapTest_{Guid.NewGuid():N}"
        };
        return connection.ConnectionString;
    }

    private static async Task<bool> DatabaseExistsAsync(string connectionString)
    {
        var connection = new SqlConnectionStringBuilder(connectionString);
        var databaseName = connection.InitialCatalog;
        connection.InitialCatalog = "master";
        await using var sql = new SqlConnection(connection.ConnectionString);
        await sql.OpenAsync();
        await using var command = sql.CreateCommand();
        command.CommandText = "SELECT DB_ID(@name)";
        command.Parameters.AddWithValue("@name", databaseName);
        return await command.ExecuteScalarAsync() is int;
    }

    private static async Task DeleteTestDatabaseAsync(string connectionString)
    {
        var connection = new SqlConnectionStringBuilder(connectionString);
        var databaseName = connection.InitialCatalog;
        if (!databaseName.StartsWith("EstacionamentoBootstrapTest_", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Refusing to delete a database not created by this test.");
        }

        SqlConnection.ClearAllPools();
        connection.InitialCatalog = "master";
        await using var sql = new SqlConnection(connection.ConnectionString);
        await sql.OpenAsync();
        await using var command = sql.CreateCommand();
        command.CommandText = $"IF DB_ID(@name) IS NOT NULL BEGIN ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]; END";
        command.Parameters.AddWithValue("@name", databaseName);
        await command.ExecuteNonQueryAsync();
    }
}
