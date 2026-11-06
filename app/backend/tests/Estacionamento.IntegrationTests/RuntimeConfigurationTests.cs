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
}
