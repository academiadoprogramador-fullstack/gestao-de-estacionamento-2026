using Estacionamento.Infrastructure.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Estacionamento.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class IdentityMigrationTests
{
    [TestMethod]
    public async Task Initial_migration_creates_only_identity_tables_in_sql_server()
    {
        var configuredConnection = Environment.GetEnvironmentVariable("TEST_MIGRATION_SQLSERVER_CONNECTION")
            ?? Environment.GetEnvironmentVariable("TEST_SQLSERVER_CONNECTION")
            ?? throw new InvalidOperationException(
                "Set TEST_SQLSERVER_CONNECTION to an isolated SQL Server test database.");
        var databaseConnection = new SqlConnectionStringBuilder(configuredConnection);
        var databaseName = databaseConnection.InitialCatalog;
        Assert.IsFalse(string.IsNullOrWhiteSpace(databaseName), "The test connection must name its isolated database.");

        var adminConnection = new SqlConnectionStringBuilder(configuredConnection)
        {
            InitialCatalog = "master"
        };
        await using (var connection = new SqlConnection(adminConnection.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            var escapedDatabaseName = databaseName.Replace("]", "]]", StringComparison.Ordinal);
            command.CommandText = $"IF DB_ID(@databaseName) IS NULL CREATE DATABASE [{escapedDatabaseName}]";
            command.Parameters.AddWithValue("@databaseName", databaseName);
            await command.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlServer(databaseConnection.ConnectionString)
            .Options;
        await using var db = new IdentityDbContext(options);
        await db.Database.MigrateAsync();

        var tables = await db.Database.SqlQueryRaw<string>(
                "SELECT TABLE_NAME AS [Value] FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'")
            .ToListAsync();

        var expectedIdentityTables = new HashSet<string>(StringComparer.Ordinal)
        {
            "AspNetRoles",
            "AspNetRoleClaims",
            "AspNetUsers",
            "AspNetUserClaims",
            "AspNetUserLogins",
            "AspNetUserRoles",
            "AspNetUserTokens",
            "__EFMigrationsHistory"
        };
        CollectionAssert.IsSubsetOf(tables.ToArray(), expectedIdentityTables.ToArray());
        CollectionAssert.IsSubsetOf(
            new[] { "AspNetUsers", "AspNetRoles", "AspNetUserRoles", "__EFMigrationsHistory" },
            tables.ToArray());
    }
}
