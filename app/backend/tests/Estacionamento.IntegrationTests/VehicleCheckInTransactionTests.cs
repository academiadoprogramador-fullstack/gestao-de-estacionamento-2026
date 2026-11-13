using System.Security.Cryptography;
using Estacionamento.Application.Abstractions;
using Estacionamento.Application.Billing;
using Estacionamento.Application.Idempotency;
using Estacionamento.Application.Parking;
using Estacionamento.Application.Security;
using Estacionamento.Domain.Parking;
using Estacionamento.Infrastructure.Identity;
using Estacionamento.Infrastructure.Persistence;
using Estacionamento.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using EmployeeRoles = Estacionamento.Infrastructure.Identity.EmployeeRoles;

namespace Estacionamento.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class VehicleCheckInTransactionTests
{
    private static readonly Guid ActorId = Guid.NewGuid();

    [TestMethod]
    public async Task Successful_check_in_commits_minimal_result_encrypted_fields_and_all_audit_events()
    {
        var connection = CreateIsolatedConnection();
        var spaceId = Guid.NewGuid();
        var rateId = Guid.NewGuid();
        try
        {
            await using var provider = BuildProvider(connection, spaceId, rateId);
            await PrepareDatabaseAsync(provider, spaceId, rateId);
            await using var scope = provider.CreateAsyncScope();
            var outcome = await scope.ServiceProvider.GetRequiredService<VehicleCheckIn>().ExecuteAsync(
                Command(Guid.NewGuid(), spaceId), CancellationToken.None);

            Assert.AreEqual(VehicleCheckInStatus.Created, outcome.Status);
            Assert.IsNotNull(outcome.Result);
            Assert.AreEqual(201, outcome.Result.HttpStatusCode);
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var attendance = await db.Attendances.SingleAsync();
            var inspection = await db.EntryInspections.SingleAsync();
            Assert.AreEqual(19.75m, attendance.DailyRateSnapshot);
            Assert.AreEqual(rateId, attendance.DailyRateId);
            Assert.IsFalse(attendance.EncryptedDocument.Contains("synthetic-document-791", StringComparison.Ordinal));
            Assert.IsFalse(inspection.EncryptedText.Contains("synthetic-inspection", StringComparison.Ordinal));
            Assert.AreEqual("synthetic-document-791", scope.ServiceProvider.GetRequiredService<IFieldProtector>()
                .Unprotect(attendance.EncryptedDocument, attendance.Id, "document", 1));
            Assert.AreEqual("synthetic-inspection", scope.ServiceProvider.GetRequiredService<IFieldProtector>()
                .Unprotect(inspection.EncryptedText, attendance.Id, "entry-inspection", 1));
            Assert.AreEqual(1, await db.Occupancies.CountAsync(item => item.SpaceId == spaceId && item.EndedAtUtc == null));
            Assert.AreEqual(3, await db.AuditEvents.CountAsync(item => item.AttendanceId == attendance.Id));
            var savedResult = await db.IdempotentResults.SingleAsync();
            Assert.AreEqual(ActorId, savedResult.ActorId);
            Assert.AreEqual("vehicle.check-in", savedResult.Operation);
            Assert.AreEqual(outcome.Result.AttendanceId, savedResult.AttendanceId);
            StringAssert.Contains(savedResult.ResultJson, outcome.Result.PublicIdentifier);
            Assert.IsFalse(savedResult.ResultJson.Contains("synthetic-document-791", StringComparison.Ordinal));
            Assert.IsFalse(savedResult.ResultJson.Contains("synthetic-inspection", StringComparison.Ordinal));
        }
        finally
        {
            await DropDatabaseAsync(connection);
        }
    }

    [TestMethod]
    public async Task Replay_returns_original_result_before_space_and_rate_checks_and_rejects_changed_content()
    {
        var connection = CreateIsolatedConnection();
        var spaceId = Guid.NewGuid();
        var rateId = Guid.NewGuid();
        var key = Guid.NewGuid();
        try
        {
            await using var provider = BuildProvider(connection, spaceId, rateId);
            await PrepareDatabaseAsync(provider, spaceId, rateId);
            var request = Command(key, spaceId);
            VehicleCheckInOutcome first;
            await using (var firstScope = provider.CreateAsyncScope())
            {
                first = await firstScope.ServiceProvider.GetRequiredService<VehicleCheckIn>()
                    .ExecuteAsync(request, CancellationToken.None);
            }

            Assert.AreEqual(VehicleCheckInStatus.Created, first.Status);
            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                await db.Database.ExecuteSqlRawAsync("UPDATE [DailyRateConfiguration] SET [CurrentRateId] = NULL WHERE [Id] = 1");

                var replay = await scope.ServiceProvider.GetRequiredService<VehicleCheckIn>()
                    .ExecuteAsync(request, CancellationToken.None);
                Assert.AreEqual(VehicleCheckInStatus.Created, replay.Status);
                Assert.IsTrue(replay.IsReplay);
                Assert.AreEqual(first.Result, replay.Result);

                var changed = request with { DocumentNumber = "different-document" };
                var conflict = await scope.ServiceProvider.GetRequiredService<VehicleCheckIn>()
                    .ExecuteAsync(changed, CancellationToken.None);
                Assert.AreEqual(VehicleCheckInStatus.IdempotencyConflict, conflict.Status);
                Assert.AreEqual(1, await db.Attendances.CountAsync());
                Assert.AreEqual(1, await db.EntryInspections.CountAsync());
                Assert.AreEqual(1, await db.Occupancies.CountAsync());
                Assert.AreEqual(3, await db.AuditEvents.CountAsync());
                Assert.AreEqual(1, await db.IdempotentResults.CountAsync());
            }
        }
        finally
        {
            await DropDatabaseAsync(connection);
        }
    }

    [TestMethod]
    public async Task Concurrent_same_key_requests_converge_on_one_committed_check_in()
    {
        var connection = CreateIsolatedConnection();
        var spaceId = Guid.NewGuid();
        var rateId = Guid.NewGuid();
        var key = Guid.NewGuid();
        try
        {
            await using var provider = BuildProvider(connection, spaceId, rateId);
            await PrepareDatabaseAsync(provider, spaceId, rateId);
            async Task<VehicleCheckInOutcome> SendAsync()
            {
                await using var scope = provider.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<VehicleCheckIn>()
                    .ExecuteAsync(Command(key, spaceId), CancellationToken.None);
            }

            var results = await Task.WhenAll(SendAsync(), SendAsync());
            Assert.IsTrue(results.All(result => result.Status == VehicleCheckInStatus.Created));
            Assert.AreEqual(1, results.Count(result => !result.IsReplay));
            Assert.AreEqual(1, results.Count(result => result.IsReplay));
            Assert.AreEqual(results[0].Result, results[1].Result);
            await using var verifyScope = provider.CreateAsyncScope();
            var db = verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            Assert.AreEqual(1, await db.Attendances.CountAsync());
            Assert.AreEqual(1, await db.EntryInspections.CountAsync());
            Assert.AreEqual(1, await db.Occupancies.CountAsync());
            Assert.AreEqual(3, await db.AuditEvents.CountAsync());
            Assert.AreEqual(1, await db.IdempotentResults.CountAsync());
        }
        finally
        {
            await DropDatabaseAsync(connection);
        }
    }

    [TestMethod]
    public async Task Concurrent_distinct_keys_for_same_space_commit_only_one_check_in()
    {
        var connection = CreateIsolatedConnection();
        var spaceId = Guid.NewGuid();
        var rateId = Guid.NewGuid();
        try
        {
            await using var provider = BuildProvider(connection, spaceId, rateId);
            await PrepareDatabaseAsync(provider, spaceId, rateId);
            async Task<VehicleCheckInOutcome> SendAsync(Guid key)
            {
                await using var scope = provider.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<VehicleCheckIn>()
                    .ExecuteAsync(Command(key, spaceId), CancellationToken.None);
            }

            var outcomes = await Task.WhenAll(SendAsync(Guid.NewGuid()), SendAsync(Guid.NewGuid()));
            CollectionAssert.AreEquivalent(
                new[] { VehicleCheckInStatus.Created, VehicleCheckInStatus.SpaceOccupied },
                outcomes.Select(outcome => outcome.Status).ToArray());
            await using var verifyScope = provider.CreateAsyncScope();
            var db = verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            Assert.AreEqual(1, await db.Attendances.CountAsync());
            Assert.AreEqual(1, await db.EntryInspections.CountAsync());
            Assert.AreEqual(1, await db.Occupancies.CountAsync());
            Assert.AreEqual(3, await db.AuditEvents.CountAsync());
            Assert.AreEqual(1, await db.IdempotentResults.CountAsync());
        }
        finally
        {
            await DropDatabaseAsync(connection);
        }
    }

    [TestMethod]
    public async Task Failure_after_each_write_stage_rolls_back_the_entire_check_in()
    {
        foreach (var failurePoint in new[] { "reservation", "attendance", "inspection", "occupancy", "audit-1", "audit-2", "audit-3", "idempotent-result" })
        {
            var connection = CreateIsolatedConnection();
            var spaceId = Guid.NewGuid();
            var rateId = Guid.NewGuid();
            try
            {
                await using var provider = BuildProvider(connection, spaceId, rateId, failurePoint);
                await PrepareDatabaseAsync(provider, spaceId, rateId);
                await using var scope = provider.CreateAsyncScope();
                await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                    scope.ServiceProvider.GetRequiredService<VehicleCheckIn>().ExecuteAsync(
                        Command(Guid.NewGuid(), spaceId), CancellationToken.None));

                var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                Assert.AreEqual(0, await db.Attendances.CountAsync(), failurePoint);
                Assert.AreEqual(0, await db.EntryInspections.CountAsync(), failurePoint);
                Assert.AreEqual(0, await db.Occupancies.CountAsync(), failurePoint);
                Assert.AreEqual(0, await db.AuditEvents.CountAsync(), failurePoint);
                Assert.AreEqual(0, await db.IdempotentResults.CountAsync(), failurePoint);
            }
            finally
            {
                await DropDatabaseAsync(connection);
            }
        }
    }

    private static ServiceProvider BuildProvider(
        SqlConnectionStringBuilder connection, Guid spaceId, Guid rateId, string? failurePoint = null)
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<IdentityDbContext>(options => options.UseSqlServer(connection.ConnectionString));
        services.AddSingleton<IUseCaseAuthorizer, RoleUseCaseAuthorizer>();
        services.AddSingleton<ICurrentActor>(new TestActor(ActorId, [EmployeeRoles.Cashier]));
        services.AddSingleton<IClock>(new TestClock());
        services.AddSingleton<IFieldProtector>(new DataProtectionFieldProtector(new EphemeralDataProtectionProvider()));
        services.AddSingleton<ICheckInContentHmac>(new CheckInContentHmac(1,
            new Dictionary<int, byte[]> { [1] = RandomNumberGenerator.GetBytes(32) }));
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IDailyRateDirectory, EfDailyRateDirectory>();
        services.AddScoped<EfAuditStore>();
        services.AddScoped<IAuditWriter>(provider => failurePoint?.StartsWith("audit-", StringComparison.Ordinal) == true
            ? new FaultingAuditWriter(provider.GetRequiredService<EfAuditStore>(), failurePoint)
            : provider.GetRequiredService<EfAuditStore>());
        services.AddScoped<EfCheckInStore>();
        services.AddScoped<ICheckInStore>(provider => failurePoint is null
            ? provider.GetRequiredService<EfCheckInStore>()
            : new FaultingCheckInStore(provider.GetRequiredService<EfCheckInStore>(), failurePoint));
        services.AddScoped<VehicleCheckIn>();
        return services.BuildServiceProvider();
    }

    private static async Task PrepareDatabaseAsync(ServiceProvider provider, Guid spaceId, Guid rateId)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.MigrateAsync();
        db.Users.Add(new EmployeeAccount
        {
            Id = ActorId,
            UserName = "check-in-operator@example.test",
            NormalizedUserName = "CHECK-IN-OPERATOR@EXAMPLE.TEST",
            Email = "check-in-operator@example.test",
            NormalizedEmail = "CHECK-IN-OPERATOR@EXAMPLE.TEST",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            IsActive = true
        });
        db.DailyRates.Add(new DailyRateRow
        {
            Id = rateId,
            Amount = 19.75m,
            PublishedAtUtc = DateTimeOffset.UtcNow,
            PublishedById = ActorId
        });
        db.ParkingSpaces.Add(new ParkingSpaceRow { Id = spaceId, Code = "A-17" });
        await db.SaveChangesAsync();
        var configuration = await db.DailyRateConfigurations.SingleAsync(item => item.Id == 1);
        configuration.CurrentRateId = rateId;
        await db.SaveChangesAsync();
    }

    private static VehicleCheckInCommand Command(Guid key, Guid spaceId) => new(
        key,
        " ab-123 ",
        " Synthetic Model ",
        " Synthetic Owner ",
        "synthetic-document-791",
        "synthetic-inspection",
        spaceId);

    private static SqlConnectionStringBuilder CreateIsolatedConnection()
    {
        var configured = Environment.GetEnvironmentVariable("TEST_SQLSERVER_CONNECTION")
            ?? throw new InvalidOperationException("Set TEST_SQLSERVER_CONNECTION to an isolated SQL Server test server.");
        return new SqlConnectionStringBuilder(configured) { InitialCatalog = $"EstacionamentoCheckIn_{Guid.NewGuid():N}" };
    }

    private static async Task DropDatabaseAsync(SqlConnectionStringBuilder connection)
    {
        await using var admin = new SqlConnection(new SqlConnectionStringBuilder(connection.ConnectionString) { InitialCatalog = "master" }.ConnectionString);
        await admin.OpenAsync();
        await using var command = admin.CreateCommand();
        command.CommandText = $"IF DB_ID(@databaseName) IS NOT NULL BEGIN ALTER DATABASE [{connection.InitialCatalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{connection.InitialCatalog}]; END";
        command.Parameters.AddWithValue("@databaseName", connection.InitialCatalog);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record TestActor(Guid? UserId, IReadOnlyCollection<string> Roles) : ICurrentActor;
    private sealed class TestClock : IClock { public DateTimeOffset UtcNow => new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero); }

    private sealed class FaultingCheckInStore(ICheckInStore inner, string failurePoint) : ICheckInStore
    {
        public Task<StoredCheckInResult?> FindIdempotentResultAsync(Guid actorId, Guid key, CancellationToken cancellationToken) =>
            inner.FindIdempotentResultAsync(actorId, key, cancellationToken);

        public Task<StoredCheckInResult?> ResolveCommittedResultAsync(Guid actorId, Guid key, CancellationToken cancellationToken) =>
            inner.ResolveCommittedResultAsync(actorId, key, cancellationToken);

        public Task<CheckInSpace?> FindSpaceAsync(Guid spaceId, CancellationToken cancellationToken) =>
            inner.FindSpaceAsync(spaceId, cancellationToken);

        public async Task ReserveIdempotencyKeyAsync(Guid actorId, Guid key, byte[] contentHmac, int algorithmVersion,
            CancellationToken cancellationToken)
        {
            await inner.ReserveIdempotencyKeyAsync(actorId, key, contentHmac, algorithmVersion, cancellationToken);
            FailAfter("reservation");
        }

        public Task<bool> ClaimSpaceAsync(Guid spaceId, byte[] expectedVersion, CancellationToken cancellationToken) =>
            inner.ClaimSpaceAsync(spaceId, expectedVersion, cancellationToken);

        public async Task AddAttendanceAsync(Attendance attendance, string encryptedDocument, Guid dailyRateId,
            decimal dailyRateAmount, DateTimeOffset enteredAtUtc, Guid operatorId, CancellationToken cancellationToken)
        {
            await inner.AddAttendanceAsync(attendance, encryptedDocument, dailyRateId, dailyRateAmount, enteredAtUtc, operatorId, cancellationToken);
            FailAfter("attendance");
        }

        public async Task AddInspectionAsync(Guid attendanceId, string encryptedText, Guid actorId,
            DateTimeOffset recordedAtUtc, CancellationToken cancellationToken)
        {
            await inner.AddInspectionAsync(attendanceId, encryptedText, actorId, recordedAtUtc, cancellationToken);
            FailAfter("inspection");
        }

        public async Task AddOccupancyAsync(Guid spaceId, Guid attendanceId, DateTimeOffset startedAtUtc,
            Guid actorId, CancellationToken cancellationToken)
        {
            await inner.AddOccupancyAsync(spaceId, attendanceId, startedAtUtc, actorId, cancellationToken);
            FailAfter("occupancy");
        }

        public async Task CompleteIdempotencyResultAsync(Guid actorId, Guid key, VehicleCheckInResult result,
            CancellationToken cancellationToken)
        {
            await inner.CompleteIdempotencyResultAsync(actorId, key, result, cancellationToken);
            FailAfter("idempotent-result");
        }

        private void FailAfter(string stage)
        {
            if (failurePoint == stage)
            {
                throw new InvalidOperationException($"Injected failure after {stage}.");
            }
        }
    }

    private sealed class FaultingAuditWriter(IAuditWriter inner, string failurePoint) : IAuditWriter
    {
        private int calls;

        public async Task AppendAsync(Guid actorId, string action, Guid? targetId, DateTimeOffset occurredAtUtc,
            IReadOnlyDictionary<string, string> metadata, CancellationToken cancellationToken)
        {
            await inner.AppendAsync(actorId, action, targetId, occurredAtUtc, metadata, cancellationToken);
            calls++;
            if (failurePoint == $"audit-{calls}")
            {
                throw new InvalidOperationException($"Injected failure after audit {calls}.");
            }
        }
    }
}
