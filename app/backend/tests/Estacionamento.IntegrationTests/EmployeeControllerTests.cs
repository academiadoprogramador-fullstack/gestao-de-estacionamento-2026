using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Estacionamento.Application.Employees;
using Estacionamento.Api.Controllers;
using Estacionamento.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Estacionamento.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class EmployeeControllerTests
{
    [TestMethod]
    public async Task Employee_routes_enforce_role_matrix_and_never_return_initial_password()
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
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/employees")).StatusCode);
            var csrf = await SessionEndpointTests.BootstrapCsrf(client);

            await LoginAsync(client, "manobrista@local.example.invalid", "TEST_VALET_PASSWORD");
            csrf = await SessionEndpointTests.BootstrapCsrf(client);
            Assert.AreEqual(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/employees")).StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, (await PostEmployeeAsync(client, csrf, "denied@example.test", "Valid!Password2026")).StatusCode);

            await LogoutAsync(client, csrf);
            csrf = await SessionEndpointTests.BootstrapCsrf(client);
            await LoginAsync(client, "caixa@local.example.invalid", "TEST_CASHIER_PASSWORD");
            csrf = await SessionEndpointTests.BootstrapCsrf(client);
            Assert.AreEqual(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/employees")).StatusCode);
            await LogoutAsync(client, csrf);
            csrf = await SessionEndpointTests.BootstrapCsrf(client);

            await LoginAsync(client, "admin@local.example.invalid", "TEST_ADMIN_PASSWORD");
            csrf = await SessionEndpointTests.BootstrapCsrf(client);
            var list = await client.GetAsync("/api/v1/employees?page=1&pageSize=10");
            Assert.AreEqual(HttpStatusCode.OK, list.StatusCode);
            var page = await list.Content.ReadFromJsonAsync<EmployeePageResponse>();
            Assert.IsNotNull(page);
            Assert.IsTrue(page.Total >= 3);
            Assert.AreEqual(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/employees?page=0")).StatusCode);

            const string initialPassword = "Initial!Employee_2026X";
            var createdResponse = await PostEmployeeAsync(client, csrf, "new-api-employee@example.test", initialPassword);
            Assert.AreEqual(HttpStatusCode.Created, createdResponse.StatusCode);
            var responseBody = await createdResponse.Content.ReadAsStringAsync();
            Assert.IsFalse(responseBody.Contains(initialPassword, StringComparison.Ordinal));
            var createdEmployee = JsonSerializer.Deserialize<EmployeeListItem>(responseBody,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.IsNotNull(createdEmployee);
            Assert.IsFalse(string.IsNullOrWhiteSpace(createdEmployee.ConcurrencyStamp));
            SessionEndpointTests.AddCsrfHeaders(client, csrf);
            var changedAccess = await client.PutAsJsonAsync($"/api/v1/employees/{createdEmployee.Id:D}/access", new
            {
                concurrencyStamp = createdEmployee.ConcurrencyStamp,
                isActive = false,
                roles = new[] { EmployeeRoles.Cashier }
            });
            Assert.AreEqual(HttpStatusCode.OK, changedAccess.StatusCode);
            var accessResponse = await changedAccess.Content.ReadFromJsonAsync<EmployeeAccessResponse>();
            Assert.IsNotNull(accessResponse);
            Assert.IsFalse(accessResponse.Employee.IsActive);
            CollectionAssert.AreEqual(new[] { EmployeeRoles.Cashier }, accessResponse.Employee.Roles.ToArray());
            Assert.AreNotEqual(createdEmployee.ConcurrencyStamp, accessResponse.ConcurrencyStamp);
            var duplicateResponse = await PostEmployeeAsync(client, csrf, "new-api-employee@example.test", initialPassword);
            Assert.AreEqual(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
            var invalidRole = await client.PostAsJsonAsync("/api/v1/employees", new
            {
                email = "invalid-role@example.test",
                initialPassword,
                roles = new[] { "owner" }
            });
            Assert.AreEqual(HttpStatusCode.BadRequest, invalidRole.StatusCode);

            using var scope = factory.Services.CreateScope();
            var manager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<EmployeeAccount>>();
            var admin = await manager.FindByEmailAsync("admin@local.example.invalid");
            Assert.IsNotNull(admin);
            var forbiddenLastAdminChange = await client.PutAsJsonAsync($"/api/v1/employees/{admin.Id}/access", new
            {
                concurrencyStamp = admin.ConcurrencyStamp,
                isActive = false,
                roles = new[] { EmployeeRoles.Administrator }
            });
            Assert.AreEqual(HttpStatusCode.Conflict, forbiddenLastAdminChange.StatusCode);
        }
        finally
        {
            await DropDatabaseAsync(connection);
        }
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

    private static async Task<HttpResponseMessage> PostEmployeeAsync(HttpClient client, string csrf, string email, string password)
    {
        SessionEndpointTests.AddCsrfHeaders(client, csrf);
        return await client.PostAsJsonAsync("/api/v1/employees", new
        {
            email,
            initialPassword = password,
            roles = new[] { EmployeeRoles.Valet }
        });
    }

    private static SqlConnectionStringBuilder CreateIsolatedConnection()
    {
        var configured = Environment.GetEnvironmentVariable("TEST_SQLSERVER_CONNECTION")
            ?? throw new InvalidOperationException("Set TEST_SQLSERVER_CONNECTION to an isolated SQL Server test server.");
        return new SqlConnectionStringBuilder(configured) { InitialCatalog = $"EstacionamentoEmployeeApi_{Guid.NewGuid():N}" };
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
