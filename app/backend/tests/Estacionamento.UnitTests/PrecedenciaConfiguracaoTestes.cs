using Estacionamento.Api.Configuracao;
using Microsoft.Extensions.Configuration;

namespace Estacionamento.UnitTests;

[TestClass]
public sealed class PrecedenciaConfiguracaoTestes
{
    [TestMethod]
    public void UserSecretsSomenteEmDevelopmentComIdentificador()
    {
        Assert.IsTrue(PrecedenciaConfiguracao.DeveCarregarUserSecrets("Development", "id-teste"));
        Assert.IsFalse(PrecedenciaConfiguracao.DeveCarregarUserSecrets("Testing", "id-teste"));
        Assert.IsFalse(PrecedenciaConfiguracao.DeveCarregarUserSecrets("Production", "id-teste"));
        Assert.IsFalse(PrecedenciaConfiguracao.DeveCarregarUserSecrets("Development", " "));
    }

    [TestMethod]
    public void VariavelDeAmbienteSobrescreveJsonEArgumentoSobrescreveVariavel()
    {
        var diretorio = Path.Combine(Path.GetTempPath(), "estacionamento-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(diretorio);

        try
        {
            File.WriteAllText(
                Path.Combine(diretorio, "appsettings.json"),
                """{ "ConfiguracaoRegional": { "Cultura": "pt-BR" } }""");
            File.WriteAllText(
                Path.Combine(diretorio, "appsettings.Development.json"),
                """{ "ConfiguracaoRegional": { "Cultura": "en-US" } }""");

            var configuracao = new ConfigurationBuilder()
                .SetBasePath(diretorio)
                .AddJsonFile("appsettings.json")
                .AddJsonFile("appsettings.Development.json")
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConfiguracaoRegional:Cultura"] = "es-ES"
                })
                .AddCommandLine(["--ConfiguracaoRegional:Cultura=fr-FR"])
                .Build();

            Assert.AreEqual("fr-FR", configuracao["ConfiguracaoRegional:Cultura"]);
        }
        finally
        {
            Directory.Delete(diretorio, recursive: true);
        }
    }

    [TestMethod]
    public void RestricaoRemoveFontesDeSecretsQuandoDesabilitada()
    {
        var configuracao = new ConfigurationManager();
        configuracao.AddJsonFile("secrets.json", optional: true);
        configuracao.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["valor"] = "explicito"
        });

        PrecedenciaConfiguracao.AplicarRestricaoDeUserSecrets(
            configuracao,
            "Development",
            "id-teste",
            variavel => "1");

        Assert.IsFalse(configuracao.Sources.Any(fonte =>
            fonte is Microsoft.Extensions.Configuration.Json.JsonConfigurationSource json &&
            json.Path == "secrets.json"));
        Assert.AreEqual("explicito", configuracao["valor"]);
    }
}
