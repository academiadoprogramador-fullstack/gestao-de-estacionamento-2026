using Estacionamento.Api.Configuration;
using Microsoft.Extensions.Configuration;

namespace Estacionamento.IntegrationTests;

[TestClass]
public sealed class RuntimeConfigurationTests
{
    [TestMethod]
    public void Required_configuration_is_rejected_before_startup_when_missing()
    {
        var configuration = new ConfigurationBuilder().Build();

        var error = Assert.ThrowsExactly<InvalidOperationException>(
            () => RuntimeConfiguration.ValidateRequired(configuration));

        StringAssert.Contains(error.Message, "ConnectionStrings:Identity");
    }

    [TestMethod]
    public void Non_https_or_non_origin_spa_values_are_rejected()
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Identity"] = "Server=localhost;Database=Test",
            ["DataProtection:KeyRingPath"] = "/tmp/test-keyring",
            ["Security:SpaOrigin"] = "http://localhost:4200"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var error = Assert.ThrowsExactly<InvalidOperationException>(
            () => RuntimeConfiguration.ValidateRequired(configuration));

        StringAssert.Contains(error.Message, "HTTPS origin");
    }

    [TestMethod]
    public void Hmac_secret_must_be_versioned_present_and_at_least_256_bits()
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Identity"] = "Server=localhost;Database=Test",
            ["DataProtection:KeyRingPath"] = "/tmp/test-keyring",
            ["Security:SpaOrigin"] = "https://localhost:4200",
            ["Idempotency:Hmac:ActiveKeyVersion"] = "2",
            ["Idempotency:Hmac:Keys:1"] = Convert.ToBase64String(new byte[32])
        };
        var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            RuntimeConfiguration.ValidateRequired(new ConfigurationBuilder().AddInMemoryCollection(values).Build()));
        StringAssert.Contains(error.Message, "active idempotency HMAC key version");

        values["Idempotency:Hmac:Keys:2"] = Convert.ToBase64String(new byte[16]);
        error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            RuntimeConfiguration.ValidateRequired(new ConfigurationBuilder().AddInMemoryCollection(values).Build()));
        StringAssert.Contains(error.Message, "at least 32 bytes");
    }
}
