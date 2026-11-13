using System.Net;
using System.Net.Http.Json;
using Estacionamento.Api.Controllers;
using Estacionamento.Application.Billing;
using Estacionamento.Infrastructure.Identity;
using Estacionamento.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Estacionamento.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class DailyRateControllerTests
{
    [TestMethod]
    public async Task Daily_rates_start_unconfigured_and_publish_versions_with_base_version_conflicts()
    {
        var connection = CreateIsolatedConnection();
        try
        {
            using var factory = new TestApplicationFactory(connectionString: connection.ConnectionString);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = true
            });
            await SessionEndpointTests.PrepareDatabaseAndAccounts(factory);
            var csrf = await SessionEndpointTests.BootstrapCsrf(client);
            await LoginAsync(client);
            csrf = await SessionEndpointTests.BootstrapCsrf(client);

            var initial = await client.GetFromJsonAsync<DailyRateConfigurationView>("/api/v1/daily-rates/current");
            Assert.IsNotNull(initial);
            Assert.IsNull(initial.CurrentRate);
            Assert.AreEqual(0, (await client.GetFromJsonAsync<DailyRatePage>("/api/v1/daily-rates"))!.Total);
            Assert.AreEqual(HttpStatusCode.BadRequest, (await PublishAsync(client, csrf, 0m, initial.Version)).StatusCode);
            Assert.AreEqual(HttpStatusCode.BadRequest, (await PublishAsync(client, csrf, 1.001m, initial.Version)).StatusCode);

            Assert.AreEqual(HttpStatusCode.Created, (await PublishAsync(client, csrf, 10.25m, initial.Version)).StatusCode);
            var afterFirst = await client.GetFromJsonAsync<DailyRateConfigurationView>("/api/v1/daily-rates/current");
            Assert.IsNotNull(afterFirst);
            Assert.IsNotNull(afterFirst.CurrentRate);
            Assert.AreEqual(10.25m, afterFirst.CurrentRate.Amount);

            var contenders = await Task.WhenAll(
                PublishAsync(client, csrf, 12.50m, afterFirst.Version),
                PublishAsync(client, csrf, 13.75m, afterFirst.Version));
            CollectionAssert.AreEquivalent(
                new[] { HttpStatusCode.Created, HttpStatusCode.Conflict },
                contenders.Select(response => response.StatusCode).ToArray());

            var history = await client.GetFromJsonAsync<DailyRatePage>("/api/v1/daily-rates?page=1&pageSize=10");
            Assert.IsNotNull(history);
            Assert.AreEqual(2, history.Total);
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            Assert.AreEqual(2, await db.AuditEvents.CountAsync(item => item.Action == "daily_rate.published"));
        }
        finally
        {
            await DropDatabaseAsync(connection);
        }
    }

    [TestMethod]
    public async Task Attendance_transaction_holds_rate_snapshot_while_publication_commits_afterward()
    {
        var connection = CreateIsolatedConnection();
        try
        {
            using var factory = new TestApplicationFactory(connectionString: connection.ConnectionString);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = true
            });
            await SessionEndpointTests.PrepareDatabaseAndAccounts(factory);
            var csrf = await SessionEndpointTests.BootstrapCsrf(client);
            await LoginAsync(client);
            csrf = await SessionEndpointTests.BootstrapCsrf(client);

            var configuration = await client.GetFromJsonAsync<DailyRateConfigurationView>("/api/v1/daily-rates/current");
            Assert.IsNotNull(configuration);
            Assert.AreEqual(HttpStatusCode.Created, (await PublishAsync(client, csrf, 8.75m, configuration.Version)).StatusCode);
            var rateA = await client.GetFromJsonAsync<DailyRateConfigurationView>("/api/v1/daily-rates/current");
            Assert.IsNotNull(rateA);
            Assert.IsNotNull(rateA.CurrentRate);

            Guid attendanceId;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                await using var transaction = await db.Database.BeginTransactionAsync();
                var directory = scope.ServiceProvider.GetRequiredService<IDailyRateDirectory>();
                var snapshot = await directory.ReadCurrentForAttendanceAsync(CancellationToken.None);
                Assert.IsNotNull(snapshot);
                Assert.AreEqual(rateA.CurrentRate.Id, snapshot.Id);
                Assert.AreEqual(8.75m, snapshot.Amount);
                var admin = await db.Users.SingleAsync(user => user.Email == "admin@local.example.invalid");
                attendanceId = Guid.NewGuid();
                db.Attendances.Add(new AttendanceRow
                {
                    Id = attendanceId,
                    PublicIdentifier = "SNAPSHOT-TEST-A",
                    NormalizedPlate = "ABC123",
                    Model = "synthetic",
                    OwnerName = "synthetic",
                    EncryptedDocument = "ciphertext",
                    DailyRateId = snapshot.Id,
                    DailyRateSnapshot = snapshot.Amount,
                    EnteredAtUtc = DateTimeOffset.UtcNow,
                    OperatorId = admin.Id,
                    State = "active"
                });
                await db.SaveChangesAsync();

                var publishTask = PublishAsync(client, csrf, 9.50m, rateA.Version);
                Assert.AreNotSame(publishTask, await Task.WhenAny(publishTask, Task.Delay(TimeSpan.FromMilliseconds(250))));
                await transaction.CommitAsync();
                Assert.AreEqual(HttpStatusCode.Created, (await publishTask).StatusCode);
            }

            using (var verifyScope = factory.Services.CreateScope())
            {
                var db = verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var attendance = await db.Attendances.SingleAsync(item => item.Id == attendanceId);
                var newCurrent = await db.DailyRateConfigurations.Include(item => item.CurrentRate).SingleAsync(item => item.Id == 1);
                Assert.AreEqual(8.75m, attendance.DailyRateSnapshot);
                Assert.AreEqual(rateA.CurrentRate.Id, attendance.DailyRateId);
                Assert.AreEqual(9.50m, newCurrent.CurrentRate!.Amount);
            }
        }
        finally
        {
            await DropDatabaseAsync(connection);
        }
    }

    private static async Task<HttpResponseMessage> PublishAsync(HttpClient client, string csrf, decimal amount, string version)
    {
        SessionEndpointTests.AddCsrfHeaders(client, csrf);
        return await client.PostAsJsonAsync("/api/v1/daily-rates", new { amount, baseVersion = version });
    }

    private static async Task LoginAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/session/login", new
        {
            email = "admin@local.example.invalid",
            password = Environment.GetEnvironmentVariable("TEST_ADMIN_PASSWORD")
                ?? throw new InvalidOperationException("Set TEST_ADMIN_PASSWORD for API integration tests.")
        });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static SqlConnectionStringBuilder CreateIsolatedConnection()
    {
        var configured = Environment.GetEnvironmentVariable("TEST_SQLSERVER_CONNECTION")
            ?? throw new InvalidOperationException("Set TEST_SQLSERVER_CONNECTION to an isolated SQL Server test server.");
        return new SqlConnectionStringBuilder(configured) { InitialCatalog = $"EstacionamentoDailyRate_{Guid.NewGuid():N}" };
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
}
