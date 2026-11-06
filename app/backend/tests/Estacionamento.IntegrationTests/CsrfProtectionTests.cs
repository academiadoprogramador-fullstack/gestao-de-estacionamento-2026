using System.Net;
using System.Net.Http.Json;
using Estacionamento.Api.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Estacionamento.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class CsrfProtectionTests
{
    [TestMethod]
    public async Task Login_rejects_missing_token_and_accepts_valid_token_and_origin()
    {
        using var factory = new TestApplicationFactory();
        using var client = CreateClient(factory);
        await SessionEndpointTests.PrepareDatabaseAndAccounts(factory);
        await SessionEndpointTests.ResetLockoutAndAssertPassword(
            factory, "manobrista@local.example.invalid", "TEST_VALET_PASSWORD");

        var tokens = await GetToken(client);
        SessionEndpointTests.AddCsrfHeaders(client, tokens.RequestToken);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        var missingToken = await Login(client, "manobrista@local.example.invalid", "TEST_VALET_PASSWORD");
        Assert.AreEqual(HttpStatusCode.BadRequest, missingToken.StatusCode);

        SessionEndpointTests.AddCsrfHeaders(client, tokens.RequestToken);
        var accepted = await Login(client, "manobrista@local.example.invalid", "TEST_VALET_PASSWORD");
        Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode);
    }

    [TestMethod]
    public async Task Login_rejects_forged_origin_even_with_valid_token()
    {
        using var factory = new TestApplicationFactory();
        using var client = CreateClient(factory);
        await SessionEndpointTests.PrepareDatabaseAndAccounts(factory);
        await SessionEndpointTests.ResetLockoutAndAssertPassword(
            factory, "manobrista@local.example.invalid", "TEST_VALET_PASSWORD");

        var tokens = await GetToken(client);
        SessionEndpointTests.AddCsrfHeaders(client, tokens.RequestToken);
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", "https://attacker.example");

        var rejected = await Login(client, "manobrista@local.example.invalid", "TEST_VALET_PASSWORD");
        Assert.AreEqual(HttpStatusCode.Forbidden, rejected.StatusCode);
        Assert.IsFalse(rejected.Headers.Contains("Set-Cookie"));
    }

    [TestMethod]
    public async Task Rejected_logout_preserves_the_existing_session()
    {
        using var factory = new TestApplicationFactory();
        using var client = CreateClient(factory);
        await SessionEndpointTests.PrepareDatabaseAndAccounts(factory);
        await SessionEndpointTests.ResetLockoutAndAssertPassword(
            factory, "manobrista@local.example.invalid", "TEST_VALET_PASSWORD");

        var tokens = await GetToken(client);
        SessionEndpointTests.AddCsrfHeaders(client, tokens.RequestToken);
        var login = await Login(client, "manobrista@local.example.invalid", "TEST_VALET_PASSWORD");
        Assert.AreEqual(HttpStatusCode.OK, login.StatusCode);

        var authenticatedToken = await GetToken(client);
        SessionEndpointTests.AddCsrfHeaders(client, authenticatedToken.RequestToken);
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", "https://attacker.example");
        var rejectedLogout = await client.PostAsync("/api/v1/session/logout", content: null);
        Assert.AreEqual(HttpStatusCode.Forbidden, rejectedLogout.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync("/api/v1/session")).StatusCode);

        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", "https://localhost");
        Assert.AreEqual(HttpStatusCode.NoContent,
            (await client.PostAsync("/api/v1/session/logout", content: null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/v1/session")).StatusCode);
    }

    private static HttpClient CreateClient(TestApplicationFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });

    private static async Task<AntiforgeryResponse> GetToken(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/session/antiforgery");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AntiforgeryResponse>())!;
    }

    private static Task<HttpResponseMessage> Login(HttpClient client, string email, string passwordEnvironmentVariable)
        => client.PostAsJsonAsync("/api/v1/session/login", new
        {
            email,
            password = Environment.GetEnvironmentVariable(passwordEnvironmentVariable)
                ?? throw new InvalidOperationException($"Set {passwordEnvironmentVariable} for API integration tests.")
        });
}
