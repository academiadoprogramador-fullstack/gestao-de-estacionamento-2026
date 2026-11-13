using Estacionamento.Infrastructure.Identity;
using Estacionamento.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Estacionamento.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class AuditStoreTests
{
    [TestMethod]
    public async Task Audit_failure_rolls_back_business_write_and_events_are_append_only()
    {
        var connection = CreateIsolatedConnection();
        var actorId = Guid.NewGuid();
        try
        {
            await using (var db = CreateContext(connection))
            {
                await db.Database.MigrateAsync();
                db.Users.Add(new EmployeeAccount
                {
                    Id = actorId,
                    UserName = "audit-test",
                    NormalizedUserName = "AUDIT-TEST"
                });
                await db.SaveChangesAsync();

                await using var transaction = await db.Database.BeginTransactionAsync();
                db.ParkingSpaces.Add(new ParkingSpaceRow { Id = Guid.NewGuid(), Code = "ROLLBACK-1" });
                await db.SaveChangesAsync();
                var store = new EfAuditStore(db);
                await Assert.ThrowsExactlyAsync<DbUpdateException>(() => store.AppendAsync(
                    Guid.NewGuid(), "space.created", null, DateTimeOffset.UtcNow,
                    new Dictionary<string, string>(), CancellationToken.None));
                await transaction.RollbackAsync();
            }

            await using (var verify = CreateContext(connection))
            {
                Assert.AreEqual(0, await verify.ParkingSpaces.CountAsync());
                Assert.AreEqual(0, await verify.AuditEvents.CountAsync());
                var store = new EfAuditStore(verify);
                await using (var transaction = await verify.Database.BeginTransactionAsync())
                {
                    verify.ParkingSpaces.Add(new ParkingSpaceRow { Id = Guid.NewGuid(), Code = "COMMITTED-1" });
                    await verify.SaveChangesAsync();
                    await store.AppendAsync(
                        actorId, "space.created", Guid.NewGuid(), DateTimeOffset.UtcNow,
                        new Dictionary<string, string> { ["version"] = "1" }, CancellationToken.None);
                    await transaction.CommitAsync();
                }

                Assert.AreEqual(1, await verify.ParkingSpaces.CountAsync());
                Assert.AreEqual(1, await verify.AuditEvents.CountAsync());
                var eventRow = await verify.AuditEvents.SingleAsync();
                eventRow.Action = "space.deleted";
                await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => verify.SaveChangesAsync());
            }
        }
        finally
        {
            await DropDatabaseAsync(connection);
        }
    }

    private static SqlConnectionStringBuilder CreateIsolatedConnection()
    {
        var configured = Environment.GetEnvironmentVariable("TEST_SQLSERVER_CONNECTION")
            ?? throw new InvalidOperationException("Set TEST_SQLSERVER_CONNECTION to an isolated SQL Server test server.");
        return new SqlConnectionStringBuilder(configured) { InitialCatalog = $"EstacionamentoAudit_{Guid.NewGuid():N}" };
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
