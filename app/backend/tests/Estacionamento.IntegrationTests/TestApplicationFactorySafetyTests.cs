using Microsoft.Data.SqlClient;

namespace Estacionamento.IntegrationTests;

[TestClass]
public sealed class TestApplicationFactorySafetyTests
{
    [TestMethod]
    [DataRow("master")]
    [DataRow("EstacionamentoDevelopment")]
    [DataRow("tempdb")]
    public void Factory_rejects_reserved_or_development_catalogs_before_startup(string catalog)
    {
        var connection = new SqlConnectionStringBuilder
        {
            DataSource = "localhost",
            InitialCatalog = catalog,
            IntegratedSecurity = true
        }.ConnectionString;

        Assert.Throws<InvalidOperationException>(() => new TestApplicationFactory(connectionString: connection));
    }

    [TestMethod]
    public void Factory_requires_a_catalog_for_its_dedicated_test_database()
    {
        var connection = new SqlConnectionStringBuilder
        {
            DataSource = "localhost",
            IntegratedSecurity = true
        }.ConnectionString;

        Assert.Throws<InvalidOperationException>(() => new TestApplicationFactory(connectionString: connection));
    }
}
