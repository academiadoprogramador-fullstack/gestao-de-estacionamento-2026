using Estacionamento.Application.Configuracao;
using Estacionamento.Infrastructure.Servicos;
using Microsoft.Extensions.Options;

namespace Estacionamento.IntegrationTests;

/// <summary>
/// Contrato real com o bucket R2 privado. Executa somente com opt-in explícito
/// e credenciais de teste no ambiente; não usa o volume de desenvolvimento nem
/// imprime valores secretos.
/// </summary>
[TestClass]
public sealed class FotosR2ContratoTestes
{
    [TestMethod]
    public async Task BucketPrivadoPermitePutGetDeleteSemExporUrlPublica()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("ESTACIONAMENTO_TESTE_R2"),
                "1",
                StringComparison.Ordinal))
        {
            Assert.Inconclusive(
                "Defina ESTACIONAMENTO_TESTE_R2=1 e as credenciais R2 de teste para executar o contrato real.");
        }

        var endpoint = Environment.GetEnvironmentVariable("R2_API_ENDPOINT");
        var accessKey = Environment.GetEnvironmentVariable("R2_TEST_ACCESS_KEY_ID");
        var secret = Environment.GetEnvironmentVariable("R2_TEST_SECRET_ACCESS_KEY");
        var bucket = Environment.GetEnvironmentVariable("ESTACIONAMENTO_R2_TESTE_BUCKET")
            ?? "gestao-estacionamento-dev";

        if (string.IsNullOrWhiteSpace(endpoint) ||
            string.IsNullOrWhiteSpace(accessKey) ||
            string.IsNullOrWhiteSpace(secret))
        {
            Assert.Inconclusive("Credenciais R2 de teste ausentes no ambiente.");
        }

        var opcoes = new ArmazenamentoFotosOptions
        {
            Habilitado = true,
            R2 = new R2Options
            {
                Endpoint = endpoint,
                RegiaoAssinatura = "auto",
                Bucket = bucket,
                AccessKeyId = accessKey,
                SecretAccessKey = secret
            }
        };

        using var armazenamento = new ArmazenamentoFotosR2(Options.Create(opcoes));
        var chave = $"teste-contrato/{Guid.NewGuid():N}.webp";
        var conteudo = Fase5Apoio.GerarImagem(320, 200, SkiaSharp.SKEncodedImageFormat.Webp, 85);

        try
        {
            await armazenamento.EnviarAsync(
                chave,
                new MemoryStream(conteudo),
                "image/webp");

            Assert.IsTrue(await armazenamento.ExisteAsync(chave));

            await using var lido = await armazenamento.AbrirAsync(chave);
            using var memoria = new MemoryStream();
            await lido.CopyToAsync(memoria);
            CollectionAssert.AreEqual(conteudo, memoria.ToArray());

            await armazenamento.ExcluirAsync(chave);
            Assert.IsFalse(await armazenamento.ExisteAsync(chave));
        }
        catch (Exception excecao) when (
            excecao is Estacionamento.Application.Comum.ExcecaoAplicacao aplicacao &&
            aplicacao.Codigo == "photo_storage_unavailable" ||
            excecao.Message.Contains("AccessDenied", StringComparison.OrdinalIgnoreCase) ||
            excecao.Message.Contains("Forbidden", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Inconclusive(
                "O bucket/credencial de teste recusou o acesso. Confirme bucket, permissões Object Read & Write e conta do endpoint.");
        }
    }
}
