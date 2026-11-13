using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Estacionamento.Api.Controllers;
using Estacionamento.Application.Billing;
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
public sealed class AttendanceControllerTests
{
    [TestMethod]
    public async Task Check_in_endpoint_validates_access_key_and_body_and_returns_only_minimal_result()
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
            Assert.AreNotEqual(HttpStatusCode.OK, (await client.GetAsync("/openapi/v1.json")).StatusCode);
            var csrf = await SessionEndpointTests.BootstrapCsrf(client);
            var key = Guid.NewGuid();
            Assert.AreEqual(HttpStatusCode.Unauthorized,
                (await PostCheckInAsync(client, csrf, key.ToString("D"), Guid.NewGuid())).StatusCode);

            await LoginAsync(client, "admin@local.example.invalid", "TEST_ADMIN_PASSWORD");
            csrf = await SessionEndpointTests.BootstrapCsrf(client);
            var space = await CreateSpaceAsync(client, csrf);
            var current = await client.GetFromJsonAsync<DailyRateConfigurationView>("/api/v1/daily-rates/current");
            Assert.IsNotNull(current);
            SessionEndpointTests.AddCsrfHeaders(client, csrf);
            Assert.AreEqual(HttpStatusCode.Created,
                (await client.PostAsJsonAsync("/api/v1/daily-rates", new { amount = 14.50m, baseVersion = current.Version })).StatusCode);

            Assert.AreEqual(HttpStatusCode.BadRequest,
                (await PostCheckInAsync(client, csrf, null, space.Id)).StatusCode);
            Assert.AreEqual(HttpStatusCode.BadRequest,
                (await PostCheckInAsync(client, csrf, "not-a-uuid", space.Id)).StatusCode);

            SessionEndpointTests.AddCsrfHeaders(client, csrf);
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
            using (var invalidRequest = new StringContent(
                JsonSerializer.Serialize(new
                {
                    plate = "ABC123",
                    model = "Model",
                    ownerName = "Owner",
                    documentNumber = "document",
                    entryInspection = "inspection",
                    parkingSpaceId = space.Id,
                    dailyRate = 1m
                }), System.Text.Encoding.UTF8, "application/json"))
            using (var invalidResponse = await client.PostAsync("/api/v1/attendances", invalidRequest))
            {
                Assert.AreEqual(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
            }

            const string sensitiveDocument = "synthetic-api-document-237";
            const string sensitiveInspection = "synthetic-api-inspection-237";
            var validKey = Guid.NewGuid().ToString("D");
            var created = await PostCheckInAsync(client, csrf, validKey, space.Id,
                document: sensitiveDocument, inspection: sensitiveInspection);
            Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
            Assert.IsNotNull(created.Headers.Location);
            Assert.AreEqual("/api/v1/attendances/" +
                (await created.Content.ReadFromJsonAsync<AttendanceCreatedResponse>())!.AttendanceId.ToString("D"),
                created.Headers.Location.OriginalString);
            var body = await created.Content.ReadAsStringAsync();
            Assert.IsFalse(body.Contains(sensitiveDocument, StringComparison.Ordinal));
            Assert.IsFalse(body.Contains(sensitiveInspection, StringComparison.Ordinal));
            var result = JsonSerializer.Deserialize<AttendanceCreatedResponse>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.IsNotNull(result);
            Assert.AreEqual(space.Id, result.ParkingSpaceId);
            Assert.AreEqual("active", result.State);

            var replay = await PostCheckInAsync(client, csrf, validKey, space.Id,
                document: sensitiveDocument, inspection: sensitiveInspection);
            Assert.AreEqual(HttpStatusCode.Created, replay.StatusCode);
            Assert.AreEqual(created.Headers.Location, replay.Headers.Location);
            Assert.AreEqual(body, await replay.Content.ReadAsStringAsync());

            var incompatible = await PostCheckInAsync(client, csrf, validKey, space.Id,
                document: "different-document", inspection: sensitiveInspection);
            Assert.AreEqual(HttpStatusCode.Conflict, incompatible.StatusCode);
            Assert.AreEqual(HttpStatusCode.Conflict,
                (await PostCheckInAsync(client, csrf, Guid.NewGuid().ToString("D"), space.Id)).StatusCode);
            Assert.AreEqual(HttpStatusCode.NotFound,
                (await PostCheckInAsync(client, csrf, Guid.NewGuid().ToString("D"), Guid.NewGuid())).StatusCode);

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                Assert.AreEqual(1, await db.Attendances.CountAsync());
                Assert.AreEqual(1, await db.EntryInspections.CountAsync());
                Assert.AreEqual(1, await db.Occupancies.CountAsync());
                Assert.AreEqual(5, await db.AuditEvents.CountAsync());
                Assert.AreEqual(3, await db.AuditEvents.CountAsync(item => item.AttendanceId == result.AttendanceId));
                Assert.AreEqual(1, await db.IdempotentResults.CountAsync());
            }

            await LogoutAsync(client, csrf);
            csrf = await SessionEndpointTests.BootstrapCsrf(client);
            await LoginAsync(client, "manobrista@local.example.invalid", "TEST_VALET_PASSWORD");
            csrf = await SessionEndpointTests.BootstrapCsrf(client);
            Assert.AreEqual(HttpStatusCode.Forbidden,
                (await PostCheckInAsync(client, csrf, Guid.NewGuid().ToString("D"), space.Id)).StatusCode);
            using (var scope = factory.Services.CreateScope())
            {
                Assert.AreEqual(1, await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Attendances.CountAsync());
            }
        }
        finally
        {
            await DropDatabaseAsync(connection);
        }
    }

    [TestMethod]
    public async Task Missing_daily_rate_returns_unprocessable_entity_without_partial_data()
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
            await LoginAsync(client, "admin@local.example.invalid", "TEST_ADMIN_PASSWORD");
            csrf = await SessionEndpointTests.BootstrapCsrf(client);
            var space = await CreateSpaceAsync(client, csrf);

            Assert.AreEqual(HttpStatusCode.UnprocessableEntity,
                (await PostCheckInAsync(client, csrf, Guid.NewGuid().ToString("D"), space.Id)).StatusCode);
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            Assert.AreEqual(0, await db.Attendances.CountAsync());
            Assert.AreEqual(0, await db.IdempotentResults.CountAsync());
            Assert.AreEqual(0, await db.Occupancies.CountAsync());
        }
        finally
        {
            await DropDatabaseAsync(connection);
        }
    }

    [TestMethod]
    public async Task Revoked_cookie_cannot_replay_a_previously_confirmed_check_in()
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
            await LoginAsync(client, "admin@local.example.invalid", "TEST_ADMIN_PASSWORD");
            csrf = await SessionEndpointTests.BootstrapCsrf(client);
            var space = await CreateSpaceAsync(client, csrf);
            var current = await client.GetFromJsonAsync<DailyRateConfigurationView>("/api/v1/daily-rates/current");
            Assert.IsNotNull(current);
            SessionEndpointTests.AddCsrfHeaders(client, csrf);
            Assert.AreEqual(HttpStatusCode.Created,
                (await client.PostAsJsonAsync("/api/v1/daily-rates", new { amount = 14.50m, baseVersion = current.Version })).StatusCode);
            await LogoutAsync(client, csrf);
            csrf = await SessionEndpointTests.BootstrapCsrf(client);
            await LoginAsync(client, "caixa@local.example.invalid", "TEST_CASHIER_PASSWORD");
            csrf = await SessionEndpointTests.BootstrapCsrf(client);

            var key = Guid.NewGuid().ToString("D");
            var successful = await PostCheckInAsync(client, csrf, key, space.Id,
                document: "synthetic-replay-document", inspection: "synthetic-replay-inspection");
            Assert.AreEqual(HttpStatusCode.Created, successful.StatusCode);

            using (var scope = factory.Services.CreateScope())
            {
                var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<EmployeeAccount>>();
                var cashier = await users.FindByEmailAsync("caixa@local.example.invalid");
                Assert.IsNotNull(cashier);
                Assert.IsTrue((await users.RemoveFromRoleAsync(cashier, EmployeeRoles.Cashier)).Succeeded);
                Assert.IsTrue((await users.AddToRoleAsync(cashier, EmployeeRoles.Valet)).Succeeded);
                Assert.IsTrue((await users.UpdateSecurityStampAsync(cashier)).Succeeded);
            }

            Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/spaces")).StatusCode);
            csrf = await SessionEndpointTests.BootstrapCsrf(client);
            var staleReplay = await PostCheckInAsync(client, csrf, key, space.Id,
                document: "synthetic-replay-document", inspection: "synthetic-replay-inspection");
            Assert.AreEqual(HttpStatusCode.Unauthorized, staleReplay.StatusCode);
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                Assert.AreEqual(1, await db.Attendances.CountAsync());
                Assert.AreEqual(1, await db.IdempotentResults.CountAsync());
                Assert.AreEqual(3, await db.AuditEvents.CountAsync(item => item.AttendanceId != null));
            }
        }
        finally
        {
            await DropDatabaseAsync(connection);
        }
    }

    [TestMethod]
    public async Task Search_returns_distinct_normalized_plate_visits_with_stable_paging_and_rejects_invalid_filters()
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
            await LoginAsync(client, "admin@local.example.invalid", "TEST_ADMIN_PASSWORD");
            csrf = await SessionEndpointTests.BootstrapCsrf(client);
            var firstSpace = await CreateSpaceAsync(client, csrf);
            SessionEndpointTests.AddCsrfHeaders(client, csrf);
            Assert.AreEqual(HttpStatusCode.Created,
                (await client.PostAsJsonAsync("/api/v1/spaces", new { code = "CHECK-IN-2" })).StatusCode);
            var secondSpace = await client.GetFromJsonAsync<SpacePageResponse>("/api/v1/spaces?code=CHECK-IN-2");
            Assert.IsNotNull(secondSpace);
            var current = await client.GetFromJsonAsync<DailyRateConfigurationView>("/api/v1/daily-rates/current");
            Assert.IsNotNull(current);
            SessionEndpointTests.AddCsrfHeaders(client, csrf);
            Assert.AreEqual(HttpStatusCode.Created,
                (await client.PostAsJsonAsync("/api/v1/daily-rates", new { amount = 14.50m, baseVersion = current.Version })).StatusCode);

            var firstResponse = await PostCheckInAsync(client, csrf, Guid.NewGuid().ToString("D"), firstSpace.Id);
            Assert.AreEqual(HttpStatusCode.Created, firstResponse.StatusCode);
            var first = await firstResponse.Content.ReadFromJsonAsync<AttendanceCreatedResponse>();
            Assert.IsNotNull(first);
            var secondResponse = await PostCheckInAsync(client, csrf, Guid.NewGuid().ToString("D"), secondSpace.Items.Single().Id);
            Assert.AreEqual(HttpStatusCode.Created, secondResponse.StatusCode);
            var second = await secondResponse.Content.ReadFromJsonAsync<AttendanceCreatedResponse>();
            Assert.IsNotNull(second);

            var search = await client.GetFromJsonAsync<AttendanceSearchPage>("/api/v1/attendances?plate=%20AB-123%20&page=1&pageSize=1");
            Assert.IsNotNull(search);
            Assert.AreEqual(2, search.Total);
            Assert.AreEqual(1, search.Items.Count);
            Assert.AreEqual(1, search.Page);
            Assert.AreEqual(1, search.PageSize);
            Assert.AreEqual("AB123", search.Items[0].Plate);
            Assert.IsTrue(new[] { first.AttendanceId, second.AttendanceId }.Contains(search.Items[0].Id));

            var secondPage = await client.GetFromJsonAsync<AttendanceSearchPage>("/api/v1/attendances?plate=AB123&page=2&pageSize=1");
            Assert.IsNotNull(secondPage);
            Assert.AreEqual(2, secondPage.Total);
            Assert.AreEqual(1, secondPage.Items.Count);
            Assert.AreNotEqual(search.Items[0].Id, secondPage.Items[0].Id);

            var byIdentifier = await client.GetFromJsonAsync<AttendanceSearchPage>(
                $"/api/v1/attendances?identifier={Uri.EscapeDataString(first.PublicIdentifier.ToLowerInvariant())}");
            Assert.IsNotNull(byIdentifier);
            Assert.AreEqual(1, byIdentifier.Total);
            Assert.AreEqual(first.AttendanceId, byIdentifier.Items.Single().Id);

            using (var adminDetailResponse = await client.GetAsync($"/api/v1/attendances/{first.AttendanceId:D}"))
            {
                Assert.AreEqual(HttpStatusCode.OK, adminDetailResponse.StatusCode);
                using var detailJson = JsonDocument.Parse(await adminDetailResponse.Content.ReadAsStringAsync());
                var detail = detailJson.RootElement;
                Assert.IsTrue(detail.TryGetProperty("dailyRateSnapshot", out _));
                Assert.IsTrue(detail.TryGetProperty("dailyRateId", out _));
                Assert.IsFalse(detail.TryGetProperty("documentNumber", out _));
                Assert.IsFalse(detail.TryGetProperty("entryInspection", out _));
                Assert.IsFalse(detail.TryGetProperty("encryptedDocument", out _));
                Assert.IsFalse(detail.TryGetProperty("rowVersion", out _));
            }
            var adminHistory = await client.GetFromJsonAsync<AttendanceHistoryPage>(
                $"/api/v1/attendances/{first.AttendanceId:D}/history");
            Assert.IsNotNull(adminHistory);
            Assert.AreEqual(3, adminHistory.Total);
            Assert.AreEqual(3, adminHistory.Items.Count);
            Assert.IsTrue(adminHistory.Items.Any(item => item.Action == "daily_rate.captured"));
            Assert.IsFalse(JsonSerializer.Serialize(adminHistory).Contains("synthetic-document", StringComparison.Ordinal));
            Assert.IsFalse(JsonSerializer.Serialize(adminHistory).Contains("synthetic-inspection", StringComparison.Ordinal));

            Assert.AreEqual(HttpStatusCode.BadRequest,
                (await client.GetAsync("/api/v1/attendances")).StatusCode);
            Assert.AreEqual(HttpStatusCode.BadRequest,
                (await client.GetAsync("/api/v1/attendances?plate=AB123&identifier=" + first.PublicIdentifier)).StatusCode);
            Assert.AreEqual(HttpStatusCode.BadRequest,
                (await client.GetAsync("/api/v1/attendances?plate=---")).StatusCode);
            Assert.AreEqual(HttpStatusCode.BadRequest,
                (await client.GetAsync("/api/v1/attendances?identifier=not-an-id")).StatusCode);
            Assert.AreEqual(HttpStatusCode.BadRequest,
                (await client.GetAsync("/api/v1/attendances?plate=AB123&page=0")).StatusCode);
            Assert.AreEqual(HttpStatusCode.BadRequest,
                (await client.GetAsync("/api/v1/attendances?plate=AB123&pageSize=101")).StatusCode);
            var empty = await client.GetFromJsonAsync<AttendanceSearchPage>("/api/v1/attendances?plate=ZZ999");
            Assert.IsNotNull(empty);
            Assert.AreEqual(0, empty.Total);
            Assert.AreEqual(0, empty.Items.Count);

            await LogoutAsync(client, csrf);
            csrf = await SessionEndpointTests.BootstrapCsrf(client);
            await LoginAsync(client, "caixa@local.example.invalid", "TEST_CASHIER_PASSWORD");
            csrf = await SessionEndpointTests.BootstrapCsrf(client);
            using (var cashierDetailResponse = await client.GetAsync($"/api/v1/attendances/{first.AttendanceId:D}"))
            {
                Assert.AreEqual(HttpStatusCode.OK, cashierDetailResponse.StatusCode);
                using var detailJson = JsonDocument.Parse(await cashierDetailResponse.Content.ReadAsStringAsync());
                Assert.IsTrue(detailJson.RootElement.TryGetProperty("dailyRateSnapshot", out _));
                Assert.IsFalse(detailJson.RootElement.TryGetProperty("documentNumber", out _));
            }
            await LogoutAsync(client, csrf);
            csrf = await SessionEndpointTests.BootstrapCsrf(client);
            await LoginAsync(client, "manobrista@local.example.invalid", "TEST_VALET_PASSWORD");
            csrf = await SessionEndpointTests.BootstrapCsrf(client);
            Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync("/api/v1/attendances?plate=AB123")).StatusCode);
            using (var valetDetailResponse = await client.GetAsync($"/api/v1/attendances/{first.AttendanceId:D}"))
            {
                Assert.AreEqual(HttpStatusCode.OK, valetDetailResponse.StatusCode);
                var valetBody = await valetDetailResponse.Content.ReadAsStringAsync();
                using var detailJson = JsonDocument.Parse(valetBody);
                var detail = detailJson.RootElement;
                Assert.IsFalse(detail.TryGetProperty("dailyRateSnapshot", out _));
                Assert.IsFalse(detail.TryGetProperty("dailyRateId", out _));
                Assert.IsFalse(detail.TryGetProperty("documentNumber", out _));
                Assert.IsFalse(valetBody.Contains("synthetic-document", StringComparison.Ordinal));
                Assert.IsFalse(valetBody.Contains("synthetic-inspection", StringComparison.Ordinal));
            }
            var valetHistory = await client.GetFromJsonAsync<AttendanceHistoryPage>(
                $"/api/v1/attendances/{first.AttendanceId:D}/history");
            Assert.IsNotNull(valetHistory);
            Assert.AreEqual(2, valetHistory.Total);
            Assert.IsFalse(valetHistory.Items.Any(item => item.Action == "daily_rate.captured"));
            Assert.IsFalse(JsonSerializer.Serialize(valetHistory).Contains("dailyRateId", StringComparison.Ordinal));
        }
        finally
        {
            await DropDatabaseAsync(connection);
        }
    }

    [TestMethod]
    public async Task Sensitive_document_and_inspection_reads_are_authorized_decrypted_audited_and_not_cached()
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
            await LoginAsync(client, "admin@local.example.invalid", "TEST_ADMIN_PASSWORD");
            csrf = await SessionEndpointTests.BootstrapCsrf(client);
            var space = await CreateSpaceAsync(client, csrf);
            var current = await client.GetFromJsonAsync<DailyRateConfigurationView>("/api/v1/daily-rates/current");
            Assert.IsNotNull(current);
            SessionEndpointTests.AddCsrfHeaders(client, csrf);
            Assert.AreEqual(HttpStatusCode.Created,
                (await client.PostAsJsonAsync("/api/v1/daily-rates", new { amount = 14.50m, baseVersion = current.Version })).StatusCode);

            const string document = "sensitive-doc-742";
            const string inspection = "sensitive-inspection-742";
            var createdResponse = await PostCheckInAsync(client, csrf, Guid.NewGuid().ToString("D"), space.Id,
                document, inspection);
            Assert.AreEqual(HttpStatusCode.Created, createdResponse.StatusCode);
            var created = await createdResponse.Content.ReadFromJsonAsync<AttendanceCreatedResponse>();
            Assert.IsNotNull(created);

            using (var documentResponse = await client.GetAsync($"/api/v1/attendances/{created.AttendanceId:D}/document"))
            {
                Assert.AreEqual(HttpStatusCode.OK, documentResponse.StatusCode);
                Assert.AreEqual("no-store", documentResponse.Headers.CacheControl?.NoStore == true ? "no-store" : null);
                var payload = await documentResponse.Content.ReadFromJsonAsync<SensitiveFieldResponse>();
                Assert.IsNotNull(payload);
                Assert.AreEqual(document, payload.Value);
            }
            using (var inspectionResponse = await client.GetAsync($"/api/v1/attendances/{created.AttendanceId:D}/entry-inspection"))
            {
                Assert.AreEqual(HttpStatusCode.OK, inspectionResponse.StatusCode);
                var payload = await inspectionResponse.Content.ReadFromJsonAsync<SensitiveFieldResponse>();
                Assert.IsNotNull(payload);
                Assert.AreEqual(inspection, payload.Value);
            }

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var readEvents = await db.AuditEvents.Where(item => item.AttendanceId == created.AttendanceId
                    && item.Action.EndsWith(".read")).ToListAsync();
                Assert.AreEqual(2, readEvents.Count);
                Assert.IsTrue(readEvents.Any(item => item.Action == "attendance.document.read"));
                Assert.IsTrue(readEvents.Any(item => item.Action == "attendance.entry-inspection.read"));
                Assert.IsTrue(readEvents.All(item => !item.MetadataJson.Contains(document, StringComparison.Ordinal)
                    && !item.MetadataJson.Contains(inspection, StringComparison.Ordinal)));
            }

            await LogoutAsync(client, csrf);
            csrf = await SessionEndpointTests.BootstrapCsrf(client);
            await LoginAsync(client, "manobrista@local.example.invalid", "TEST_VALET_PASSWORD");
            Assert.AreEqual(HttpStatusCode.Forbidden,
                (await client.GetAsync($"/api/v1/attendances/{created.AttendanceId:D}/document")).StatusCode);
            using (var valetInspection = await client.GetAsync($"/api/v1/attendances/{created.AttendanceId:D}/entry-inspection"))
            {
                Assert.AreEqual(HttpStatusCode.OK, valetInspection.StatusCode);
                Assert.AreEqual(inspection,
                    (await valetInspection.Content.ReadFromJsonAsync<SensitiveFieldResponse>())!.Value);
            }

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                Assert.AreEqual(3, await db.AuditEvents.CountAsync(item => item.AttendanceId == created.AttendanceId
                    && item.Action.EndsWith(".read")));
            }
        }
        finally
        {
            await DropDatabaseAsync(connection);
        }
    }

    [TestMethod]
    public async Task Sensitive_value_is_not_returned_when_decryption_or_audit_fails()
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
            await LoginAsync(client, "admin@local.example.invalid", "TEST_ADMIN_PASSWORD");
            csrf = await SessionEndpointTests.BootstrapCsrf(client);
            var space = await CreateSpaceAsync(client, csrf);
            var current = await client.GetFromJsonAsync<DailyRateConfigurationView>("/api/v1/daily-rates/current");
            Assert.IsNotNull(current);
            SessionEndpointTests.AddCsrfHeaders(client, csrf);
            Assert.AreEqual(HttpStatusCode.Created,
                (await client.PostAsJsonAsync("/api/v1/daily-rates", new { amount = 14.50m, baseVersion = current.Version })).StatusCode);
            const string document = "sensitive-failure-doc-817";
            const string inspection = "sensitive-failure-inspection-817";
            var response = await PostCheckInAsync(client, csrf, Guid.NewGuid().ToString("D"), space.Id, document, inspection);
            Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
            var created = await response.Content.ReadFromJsonAsync<AttendanceCreatedResponse>();
            Assert.IsNotNull(created);

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE [Attendances] SET [EncryptedDocument] = N'invalid-ciphertext' WHERE [Id] = {created.AttendanceId}");
            }
            using (var failedDecrypt = await client.GetAsync($"/api/v1/attendances/{created.AttendanceId:D}/document"))
            {
                Assert.AreEqual(HttpStatusCode.InternalServerError, failedDecrypt.StatusCode);
                var body = await failedDecrypt.Content.ReadAsStringAsync();
                Assert.IsFalse(body.Contains(document, StringComparison.Ordinal));
                Assert.IsFalse(body.Contains("invalid-ciphertext", StringComparison.Ordinal));
            }
            using (var verifyScope = factory.Services.CreateScope())
            {
                Assert.AreEqual(0, await verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>()
                    .AuditEvents.CountAsync(item => item.AttendanceId == created.AttendanceId
                        && item.Action == "attendance.document.read"));
            }

            await LogoutAsync(client, csrf);
            using (var failedAuditFactory = new TestApplicationFactory(
                connectionString: connection.ConnectionString,
                failSensitiveReadAudit: true))
            using (var failedAuditClient = failedAuditFactory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = true
            }))
            {
                await SessionEndpointTests.PrepareDatabaseAndAccounts(failedAuditFactory);
                var auditCsrf = await SessionEndpointTests.BootstrapCsrf(failedAuditClient);
                SessionEndpointTests.AddCsrfHeaders(failedAuditClient, auditCsrf);
                await LoginAsync(failedAuditClient, "admin@local.example.invalid", "TEST_ADMIN_PASSWORD");
                var failedAudit = await failedAuditClient.GetAsync($"/api/v1/attendances/{created.AttendanceId:D}/entry-inspection");
                Assert.AreEqual(HttpStatusCode.InternalServerError, failedAudit.StatusCode);
                var body = await failedAudit.Content.ReadAsStringAsync();
                Assert.IsFalse(body.Contains(inspection, StringComparison.Ordinal));
            }
        }
        finally
        {
            await DropDatabaseAsync(connection);
        }
    }

    private static async Task<ParkingSpaceView> CreateSpaceAsync(HttpClient client, string csrf)
    {
        SessionEndpointTests.AddCsrfHeaders(client, csrf);
        var response = await client.PostAsJsonAsync("/api/v1/spaces", new { code = "CHECK-IN-1" });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        var space = await response.Content.ReadFromJsonAsync<ParkingSpaceView>();
        Assert.IsNotNull(space);
        return space;
    }

    private static async Task<HttpResponseMessage> PostCheckInAsync(
        HttpClient client, string csrf, string? key, Guid spaceId,
        string document = "synthetic-document", string inspection = "synthetic-inspection")
    {
        SessionEndpointTests.AddCsrfHeaders(client, csrf);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        if (key is not null)
        {
            client.DefaultRequestHeaders.Add("Idempotency-Key", key);
        }

        return await client.PostAsJsonAsync("/api/v1/attendances", new
        {
            plate = " ab-123 ",
            model = "Synthetic Model",
            ownerName = "Synthetic Owner",
            documentNumber = document,
            entryInspection = inspection,
            parkingSpaceId = spaceId
        });
    }

    private static async Task LoginAsync(HttpClient client, string email, string environmentVariable)
    {
        var password = Environment.GetEnvironmentVariable(environmentVariable)
            ?? throw new InvalidOperationException($"Set {environmentVariable} for API integration tests.");
        var response = await client.PostAsJsonAsync("/api/v1/session/login", new { email, password });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task LogoutAsync(HttpClient client, string csrf)
    {
        SessionEndpointTests.AddCsrfHeaders(client, csrf);
        Assert.AreEqual(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/session/logout", null)).StatusCode);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
    }

    private static SqlConnectionStringBuilder CreateIsolatedConnection()
    {
        var configured = Environment.GetEnvironmentVariable("TEST_SQLSERVER_CONNECTION")
            ?? throw new InvalidOperationException("Set TEST_SQLSERVER_CONNECTION to an isolated SQL Server test server.");
        return new SqlConnectionStringBuilder(configured) { InitialCatalog = $"EstacionamentoAttendanceApi_{Guid.NewGuid():N}" };
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
