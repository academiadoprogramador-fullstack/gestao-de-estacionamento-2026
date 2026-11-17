using System.Text.Json;

namespace Estacionamento.UnitTests;

[TestClass]
public sealed class ConfiguracaoVersionadaTestes
{
    private static string DiretorioApi()
    {
        var diretorio = new DirectoryInfo(AppContext.BaseDirectory);
        while (diretorio is not null)
        {
            var candidato = Path.Combine(
                diretorio.FullName, "app", "backend", "src", "Estacionamento.Api");
            if (Directory.Exists(candidato))
            {
                return candidato;
            }

            diretorio = diretorio.Parent;
        }

        throw new DirectoryNotFoundException("Não foi possível localizar o projeto da API.");
    }

    [DataTestMethod]
    [DataRow("appsettings.json")]
    [DataRow("appsettings.Development.json")]
    public void ArquivosVersionadosNaoContemSegredosNemContasSinteticas(string arquivo)
    {
        var caminho = Path.Combine(DiretorioApi(), arquivo);
        Assert.IsTrue(File.Exists(caminho));
        var conteudo = File.ReadAllText(caminho);

        Assert.IsFalse(conteudo.Contains("ContasSinteticas", StringComparison.Ordinal));
        Assert.IsFalse(conteudo.Contains("Password=", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(conteudo.Contains("\"Email\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(conteudo.Contains("\"Senha\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(conteudo.Contains("AccessKey", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(conteudo.Contains("SecretAccessKey", StringComparison.OrdinalIgnoreCase));

        using var documento = JsonDocument.Parse(conteudo);
        var raiz = documento.RootElement;

        Assert.IsTrue(raiz.GetProperty("Autenticacao").GetProperty("Jwt")
            .TryGetProperty("VersaoAtiva", out _));
        Assert.IsTrue(raiz.GetProperty("ProtecaoDados").TryGetProperty("VersaoAtiva", out _));

        if (raiz.GetProperty("Autenticacao").GetProperty("Jwt").TryGetProperty("Chaves", out _))
        {
            Assert.Fail("Valores de chaves JWT não podem ser versionados.");
        }

        if (raiz.GetProperty("ProtecaoDados").TryGetProperty("Chaves", out _))
        {
            Assert.Fail("Valores de chaves AES não podem ser versionados.");
        }
    }

    [TestMethod]
    public void TemplateDeUserSecretsTemSomentePlaceholders()
    {
        var diretorio = new DirectoryInfo(AppContext.BaseDirectory);
        string? readme = null;

        while (diretorio is not null)
        {
            var candidato = Path.Combine(diretorio.FullName, "README.md");
            if (File.Exists(candidato))
            {
                readme = candidato;
                break;
            }

            diretorio = diretorio.Parent;
        }

        Assert.IsNotNull(readme);
        var conteudo = File.ReadAllText(readme);

        StringAssert.Contains(conteudo, "<CONEXAO_SQL_LOCAL_COM_USUARIO_E_SENHA_DA_API>");
        StringAssert.Contains(conteudo, "<CHAVE_JWT_ALEATORIA_DE_32_BYTES_EM_BASE64>");
        StringAssert.Contains(conteudo, "<CHAVE_AES_ALEATORIA_DE_32_BYTES_EM_BASE64>");
        Assert.IsFalse(conteudo.Contains("\"Senha\": \"<", StringComparison.Ordinal) &&
            conteudo.Contains("ContasSinteticas\": {\n    \"Administrador\": {\n      \"Email\": \"admin", StringComparison.Ordinal));
    }
}
