using Estacionamento.Infrastructure.Identity;
using Estacionamento.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Estacionamento.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class OperationalModelTests
{
    [TestMethod]
    public async Task Sql_server_rejects_two_open_occupancies_for_one_space()
    {
        var configured = Environment.GetEnvironmentVariable("TEST_SQLSERVER_CONNECTION")
            ?? throw new InvalidOperationException("Set TEST_SQLSERVER_CONNECTION to an isolated SQL Server test server.");
        var connection = new SqlConnectionStringBuilder(configured)
        {
            InitialCatalog = $"EstacionamentoModel_{Guid.NewGuid():N}"
        };
        var options = new DbContextOptionsBuilder<IdentityDbContext>().UseSqlServer(connection.ConnectionString).Options;

        try
        {
            await using var db = new IdentityDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var actorId = Guid.NewGuid();
            var rateId = Guid.NewGuid();
            var spaceId = Guid.NewGuid();
            var firstAttendanceId = Guid.NewGuid();
            var secondAttendanceId = Guid.NewGuid();
            db.Users.Add(new EmployeeAccount { Id = actorId, UserName = "model-test", NormalizedUserName = "MODEL-TEST" });
            db.DailyRates.Add(new DailyRateRow { Id = rateId, Amount = 12.34m, PublishedAtUtc = DateTimeOffset.UtcNow, PublishedById = actorId });
            db.ParkingSpaces.Add(new ParkingSpaceRow { Id = spaceId, Code = "A-1" });
            db.Attendances.AddRange(
                Attendance(firstAttendanceId, actorId, rateId, "MODEL-1"),
                Attendance(secondAttendanceId, actorId, rateId, "MODEL-2"));
            await db.SaveChangesAsync();

            db.Occupancies.Add(new OccupancyRow
            {
                Id = Guid.NewGuid(),
                SpaceId = spaceId,
                AttendanceId = firstAttendanceId,
                StartedAtUtc = DateTimeOffset.UtcNow,
                ActorId = actorId
            });
            await db.SaveChangesAsync();

            db.Occupancies.Add(new OccupancyRow
            {
                Id = Guid.NewGuid(),
                SpaceId = spaceId,
                AttendanceId = secondAttendanceId,
                StartedAtUtc = DateTimeOffset.UtcNow,
                ActorId = actorId
            });
            var exception = await Assert.ThrowsExactlyAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.IsTrue(exception.InnerException is SqlException { Number: 2601 or 2627 });
        }
        finally
        {
            await using var admin = new SqlConnection(new SqlConnectionStringBuilder(connection.ConnectionString)
            {
                InitialCatalog = "master"
            }.ConnectionString);
            await admin.OpenAsync();
            await using var command = admin.CreateCommand();
            command.CommandText = $"ALTER DATABASE [{connection.InitialCatalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{connection.InitialCatalog}]";
            await command.ExecuteNonQueryAsync();
        }
    }

    [TestMethod]
    public async Task Idempotency_key_scope_allows_same_key_for_different_actors_only()
    {
        var configured = Environment.GetEnvironmentVariable("TEST_SQLSERVER_CONNECTION")
            ?? throw new InvalidOperationException("Set TEST_SQLSERVER_CONNECTION to an isolated SQL Server test server.");
        var connection = new SqlConnectionStringBuilder(configured)
        {
            InitialCatalog = $"EstacionamentoIdempotencyModel_{Guid.NewGuid():N}"
        };
        var options = new DbContextOptionsBuilder<IdentityDbContext>().UseSqlServer(connection.ConnectionString).Options;

        try
        {
            await using var db = new IdentityDbContext(options);
            await db.Database.EnsureCreatedAsync();
            var firstActor = Guid.NewGuid();
            var secondActor = Guid.NewGuid();
            var key = Guid.NewGuid();
            db.Users.AddRange(
                new EmployeeAccount { Id = firstActor, UserName = "idempotency-one", NormalizedUserName = "IDEMPOTENCY-ONE" },
                new EmployeeAccount { Id = secondActor, UserName = "idempotency-two", NormalizedUserName = "IDEMPOTENCY-TWO" });
            await db.SaveChangesAsync();
            db.IdempotentResults.AddRange(
                IdempotentResult(firstActor, key),
                IdempotentResult(secondActor, key));
            await db.SaveChangesAsync();
            Assert.AreEqual(2, await db.IdempotentResults.CountAsync());

            db.IdempotentResults.Add(IdempotentResult(firstActor, key));
            var exception = await Assert.ThrowsExactlyAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.IsTrue(exception.InnerException is SqlException { Number: 2601 or 2627 });
        }
        finally
        {
            await using var admin = new SqlConnection(new SqlConnectionStringBuilder(connection.ConnectionString)
            {
                InitialCatalog = "master"
            }.ConnectionString);
            await admin.OpenAsync();
            await using var command = admin.CreateCommand();
            command.CommandText = $"IF DB_ID(@databaseName) IS NOT NULL BEGIN ALTER DATABASE [{connection.InitialCatalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{connection.InitialCatalog}]; END";
            command.Parameters.AddWithValue("@databaseName", connection.InitialCatalog);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static IdempotentResultRow IdempotentResult(Guid actorId, Guid key) => new()
    {
        Id = Guid.NewGuid(),
        ActorId = actorId,
        Operation = "vehicle.check-in",
        Key = key,
        ContentHmac = new byte[32],
        AlgorithmVersion = 1,
        ResultJson = "{}"
    };

    private static AttendanceRow Attendance(Guid id, Guid actorId, Guid rateId, string identifier) => new()
    {
        Id = id,
        PublicIdentifier = identifier,
        NormalizedPlate = "ABC123",
        Model = "synthetic model",
        OwnerName = "synthetic owner",
        EncryptedDocument = "synthetic ciphertext",
        DailyRateId = rateId,
        DailyRateSnapshot = 12.34m,
        EnteredAtUtc = DateTimeOffset.UtcNow,
        OperatorId = actorId,
        State = "active"
    };
}
