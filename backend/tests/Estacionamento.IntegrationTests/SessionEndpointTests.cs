using System.Net;
using System.Net.Http.Json;
using Estacionamento.Api.Controllers;
using Estacionamento.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Estacionamento.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class SessionEndpointTests
{
    [TestMethod]
    public async Task Provisioned_employee_can_log_in_check_session_and_log_out_with_host_only_secure_cookie()
    {
        using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });
        await PrepareDatabaseAndAccounts(factory);
        await ResetLockoutAndAssertPassword(factory, "manobrista@local.example.invalid", "TEST_VALET_PASSWORD");

        var csrfResponse = await client.GetAsync("/api/v1/session/antiforgery");
        Assert.AreEqual(HttpStatusCode.OK, csrfResponse.StatusCode);
        var csrfCookie = string.Join(";", csrfResponse.Headers.GetValues("Set-Cookie"));
        StringAssert.Contains(csrfCookie.ToLowerInvariant(), "secure");
        StringAssert.Contains(csrfCookie.ToLowerInvariant(), "httponly");
        var csrf = await csrfResponse.Content.ReadFromJsonAsync<AntiforgeryResponse>();
        Assert.IsNotNull(csrf);
        AddCsrfHeaders(client, csrf.RequestToken);

        var loginResponse = await client.PostAsJsonAsync("/api/v1/session/login", new
        {
            email = "manobrista@local.example.invalid",
            password = RequiredEnvironmentVariable("TEST_VALET_PASSWORD")
        });
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode,
            await loginResponse.Content.ReadAsStringAsync());
        var authCookie = string.Join(";", loginResponse.Headers.GetValues("Set-Cookie"));
        StringAssert.Contains(authCookie.ToLowerInvariant(), "path=/api");
        StringAssert.Contains(authCookie.ToLowerInvariant(), "httponly");
        StringAssert.Contains(authCookie.ToLowerInvariant(), "secure");
        Assert.IsFalse(authCookie.Contains("Domain=", StringComparison.OrdinalIgnoreCase));

        var sessionResponse = await client.GetAsync("/api/v1/session");
        Assert.AreEqual(HttpStatusCode.OK, sessionResponse.StatusCode);
        Assert.IsTrue(sessionResponse.Headers.CacheControl?.NoStore);
        Assert.IsTrue(sessionResponse.Headers.CacheControl?.Private);
        var session = await sessionResponse.Content.ReadFromJsonAsync<SessionResponse>();
        Assert.IsNotNull(session);
        CollectionAssert.Contains(session.Roles.ToArray(), EmployeeRoles.Valet);

        await BootstrapCsrf(client);
        var logoutResponse = await client.PostAsync("/api/v1/session/logout", content: null);
        Assert.AreEqual(HttpStatusCode.NoContent, logoutResponse.StatusCode);
        var afterLogout = await client.GetAsync("/api/v1/session");
        Assert.AreEqual(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [TestMethod]
    public async Task Invalid_credentials_return_indistinguishable_unauthorized_response()
    {
        using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });
        await BootstrapCsrf(client);
        await PrepareDatabaseAndAccounts(factory);

        var unknown = await client.PostAsJsonAsync("/api/v1/session/login", new
        {
            email = "unknown@local.example.invalid",
            password = "Invalid!2345"
        });
        var invalidPassword = await client.PostAsJsonAsync("/api/v1/session/login", new
        {
            email = "caixa@local.example.invalid",
            password = "Invalid!2345"
        });

        Assert.AreEqual(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, invalidPassword.StatusCode);
        var unknownBody = await unknown.Content.ReadAsStringAsync();
        var invalidBody = await invalidPassword.Content.ReadAsStringAsync();
        Assert.AreEqual(unknownBody, invalidBody);
        Assert.IsTrue(unknown.Headers.CacheControl?.NoStore);
        Assert.IsTrue(invalidPassword.Headers.CacheControl?.NoStore);
        Assert.IsFalse(invalidBody.Contains("Invalid!2345", StringComparison.Ordinal));
        var logs = string.Join("\n", factory.LogCapture.Entries);
        Assert.IsFalse(logs.Contains("Invalid!2345", StringComparison.Ordinal));
        Assert.IsFalse(logs.Contains("Estacionamento.Session=", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Session_expires_after_the_configured_idle_timeout()
    {
        using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });
        await BootstrapCsrf(client);
        await PrepareDatabaseAndAccounts(factory);
        await ResetLockoutAndAssertPassword(factory, "manobrista@local.example.invalid", "TEST_VALET_PASSWORD");

        var loginResponse = await client.PostAsJsonAsync("/api/v1/session/login", new
        {
            email = "manobrista@local.example.invalid",
            password = RequiredEnvironmentVariable("TEST_VALET_PASSWORD")
        });
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode,
            await loginResponse.Content.ReadAsStringAsync());

        await Task.Delay(TimeSpan.FromSeconds(3));

        var expiredSession = await client.GetAsync("/api/v1/session");
        Assert.AreEqual(HttpStatusCode.Unauthorized, expiredSession.StatusCode);
    }

    [TestMethod]
    public async Task Administrator_route_uses_default_deny_roles_and_ignores_forged_identity_headers()
    {
        using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });
        await BootstrapCsrf(client);
        await PrepareDatabaseAndAccounts(factory);
        await ResetLockoutAndAssertPassword(factory, "caixa@local.example.invalid", "TEST_CASHIER_PASSWORD");
        await ResetLockoutAndAssertPassword(factory, "admin@local.example.invalid", "TEST_ADMIN_PASSWORD");

        client.DefaultRequestHeaders.Add("X-User", "admin@local.example.invalid");
        client.DefaultRequestHeaders.Add("X-Role", EmployeeRoles.Administrator);
        var anonymous = await client.GetAsync("/api/v1/session/admin/verify");
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var cashierLogin = await client.PostAsJsonAsync("/api/v1/session/login", new
        {
            email = "caixa@local.example.invalid",
            password = RequiredEnvironmentVariable("TEST_CASHIER_PASSWORD")
        });
        Assert.AreEqual(HttpStatusCode.OK, cashierLogin.StatusCode);
        var cashierAccess = await client.GetAsync("/api/v1/session/admin/verify");
        Assert.AreEqual(HttpStatusCode.Forbidden, cashierAccess.StatusCode);

        await BootstrapCsrf(client);
        Assert.AreEqual(HttpStatusCode.NoContent,
            (await client.PostAsync("/api/v1/session/logout", content: null)).StatusCode);
        await BootstrapCsrf(client);
        var adminLogin = await client.PostAsJsonAsync("/api/v1/session/login", new
        {
            email = "admin@local.example.invalid",
            password = RequiredEnvironmentVariable("TEST_ADMIN_PASSWORD")
        });
        Assert.AreEqual(HttpStatusCode.OK, adminLogin.StatusCode);
        var adminAccess = await client.GetAsync("/api/v1/session/admin/verify");
        Assert.AreEqual(HttpStatusCode.OK, adminAccess.StatusCode);
    }

    internal static async Task PrepareDatabaseAndAccounts(TestApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await database.Database.MigrateAsync();
        var provisioner = scope.ServiceProvider.GetRequiredService<SyntheticAccountProvisioner>();
        await provisioner.ProvisionAsync(scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>());
    }

    internal static async Task<string> BootstrapCsrf(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/session/antiforgery");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var token = await response.Content.ReadFromJsonAsync<AntiforgeryResponse>();
        Assert.IsNotNull(token);
        AddCsrfHeaders(client, token.RequestToken);
        return token.RequestToken;
    }

    internal static void AddCsrfHeaders(HttpClient client, string requestToken)
    {
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("Origin", "https://localhost");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", requestToken);
    }

    internal static async Task ResetLockoutAndAssertPassword(
        TestApplicationFactory factory,
        string email,
        string passwordEnvironmentVariable)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<EmployeeAccount>>();
        var account = await users.FindByEmailAsync(email);
        Assert.IsNotNull(account);
        account.IsActive = true;
        var password = RequiredEnvironmentVariable(passwordEnvironmentVariable);
        Assert.IsTrue(await users.CheckPasswordAsync(account, password), "The persisted synthetic password does not match the test input.");
        var signIn = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.SignInManager<EmployeeAccount>>();
        Assert.IsTrue(await signIn.CanSignInAsync(account), "Identity does not consider this account eligible for sign-in.");
        await users.ResetAccessFailedCountAsync(account);
        await users.SetLockoutEndDateAsync(account, null);
        var passwordResult = await signIn.CheckPasswordSignInAsync(account, password, lockoutOnFailure: false);
        Assert.IsTrue(passwordResult.Succeeded, "Identity password sign-in check rejected the persisted test password.");
    }

    private static string RequiredEnvironmentVariable(string name)
        => Environment.GetEnvironmentVariable(name)
            ?? throw new InvalidOperationException($"Set {name} for API integration tests.");
}
