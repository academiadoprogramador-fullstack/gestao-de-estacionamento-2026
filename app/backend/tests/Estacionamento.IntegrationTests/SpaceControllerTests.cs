using System.Net;
using System.Net.Http.Json;
using Estacionamento.Api.Controllers;
using Estacionamento.Application.Parking;
using Estacionamento.Infrastructure.Identity;
using Estacionamento.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Estacionamento.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class SpaceControllerTests
{
    [TestMethod]
    public async Task Spaces_are_normalized_unique_filterable_paged_and_readable_by_all_roles()
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
            var guestCsrf = await SessionEndpointTests.BootstrapCsrf(client);
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/spaces")).StatusCode);
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await CreateSpaceAsync(client, guestCsrf, "GUEST-1")).StatusCode);
            await LoginAsync(client, "admin@local.example.invalid", "TEST_ADMIN_PASSWORD");
            var csrf = await SessionEndpointTests.BootstrapCsrf(client);

            var first = await CreateSpaceAsync(client, csrf, "  a-01 ");
            Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
            var second = await CreateSpaceAsync(client, csrf, "B-02");
            Assert.AreEqual(HttpStatusCode.Created, second.StatusCode);
            Assert.AreEqual(HttpStatusCode.Conflict, (await CreateSpaceAsync(client, csrf, "A-01")).StatusCode);

            var filtered = await client.GetFromJsonAsync<SpacePageResponse>("/api/v1/spaces?code=a-01");
            Assert.IsNotNull(filtered);
            Assert.AreEqual(1, filtered.Total);
            Assert.AreEqual("A-01", filtered.Items.Single().Code);
            Assert.IsTrue(!string.IsNullOrWhiteSpace(filtered.Items.Single().Version));

            var paged = await client.GetFromJsonAsync<SpacePageResponse>("/api/v1/spaces?page=1&pageSize=1");
            Assert.IsNotNull(paged);
            Assert.AreEqual(2, paged.Total);
            Assert.AreEqual(1, paged.Items.Count);
            Assert.AreEqual(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/spaces?status=unknown")).StatusCode);

            var firstSpace = filtered.Items.Single();
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var admin = await db.Users.SingleAsync(user => user.Email == "admin@local.example.invalid");
                var rate = new DailyRateRow
                {
                    Id = Guid.NewGuid(),
                    Amount = 5m,
                    PublishedAtUtc = DateTimeOffset.UtcNow,
                    PublishedById = admin.Id
                };
                var attendance = new AttendanceRow
                {
                    Id = Guid.NewGuid(),
                    PublicIdentifier = "TEST-SPACE-ATTENDANCE",
                    NormalizedPlate = "ABC123",
                    Model = "synthetic",
                    OwnerName = "synthetic",
                    EncryptedDocument = "ciphertext",
                    DailyRateId = rate.Id,
                    DailyRateSnapshot = rate.Amount,
                    EnteredAtUtc = DateTimeOffset.UtcNow,
                    OperatorId = admin.Id,
                    State = "active"
                };
                db.DailyRates.Add(rate);
                db.Attendances.Add(attendance);
                db.Occupancies.Add(new OccupancyRow
                {
                    Id = Guid.NewGuid(),
                    SpaceId = firstSpace.Id,
                    AttendanceId = attendance.Id,
                    StartedAtUtc = DateTimeOffset.UtcNow,
                    ActorId = admin.Id
                });
                await db.SaveChangesAsync();
            }

            Assert.AreEqual(1, (await client.GetFromJsonAsync<SpacePageResponse>("/api/v1/spaces?status=occupied"))!.Total);
            Assert.AreEqual(1, (await client.GetFromJsonAsync<SpacePageResponse>("/api/v1/spaces?status=free"))!.Total);

            await LogoutAsync(client, csrf);
            csrf = await SessionEndpointTests.BootstrapCsrf(client);
            await LoginAsync(client, "caixa@local.example.invalid", "TEST_CASHIER_PASSWORD");
            csrf = await SessionEndpointTests.BootstrapCsrf(client);
            Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync("/api/v1/spaces")).StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, (await CreateSpaceAsync(client, csrf, "C-03")).StatusCode);
            await LogoutAsync(client, csrf);
            await SessionEndpointTests.BootstrapCsrf(client);
            await LoginAsync(client, "manobrista@local.example.invalid", "TEST_VALET_PASSWORD");
            Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync("/api/v1/spaces")).StatusCode);
        }
        finally
        {
            await DropDatabaseAsync(connection);
        }
    }

    private static async Task<HttpResponseMessage> CreateSpaceAsync(HttpClient client, string csrf, string code)
    {
        SessionEndpointTests.AddCsrfHeaders(client, csrf);
        return await client.PostAsJsonAsync("/api/v1/spaces", new { code });
    }

    private static async Task LoginAsync(HttpClient client, string email, string passwordEnvironmentVariable)
    {
        var response = await client.PostAsJsonAsync("/api/v1/session/login", new
        {
            email,
            password = Environment.GetEnvironmentVariable(passwordEnvironmentVariable)
                ?? throw new InvalidOperationException($"Set {passwordEnvironmentVariable} for API integration tests.")
        });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task LogoutAsync(HttpClient client, string csrf)
    {
        SessionEndpointTests.AddCsrfHeaders(client, csrf);
        Assert.AreEqual(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/session/logout", null)).StatusCode);
    }

    private static SqlConnectionStringBuilder CreateIsolatedConnection()
    {
        var configured = Environment.GetEnvironmentVariable("TEST_SQLSERVER_CONNECTION")
            ?? throw new InvalidOperationException("Set TEST_SQLSERVER_CONNECTION to an isolated SQL Server test server.");
        return new SqlConnectionStringBuilder(configured) { InitialCatalog = $"EstacionamentoSpaceApi_{Guid.NewGuid():N}" };
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
