using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SkiaSharp;

namespace Estacionamento.IntegrationTests;

/// <summary>
/// Utilitários da Fase 5. As imagens são sintéticas e geradas em memória; o
/// cliente nunca fornece ator, chave de objeto, bucket ou horário.
/// </summary>
internal static class Fase5Apoio
{
    public static FabricaApiTeste NovaFabrica(
        Dictionary<string, string?>? configuracaoExtra = null) =>
        Fase3Apoio.NovaFabrica(configuracaoExtra);

    public static byte[] GerarImagem(
        int largura = 640,
        int altura = 480,
        SKEncodedImageFormat formato = SKEncodedImageFormat.Jpeg,
        int qualidade = 85)
    {
        using var bitmap = new SKBitmap(largura, altura, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(12, 24, 40));
            using var pincel = new SKPaint { Color = new SKColor(180, 200, 230), IsAntialias = true };
            canvas.DrawRect(new SKRect(20, 20, largura - 20, altura - 20), pincel);
            using var texto = new SKPaint { Color = new SKColor(20, 30, 50), IsAntialias = true };
            using var fonte = new SKFont(SKTypeface.Default, 24);
            canvas.DrawText("Foto sintética de vistoria", 40, altura / 2f, fonte, texto);
        }

        using var imagem = SKImage.FromBitmap(bitmap);
        using var data = imagem.Encode(formato, qualidade);
        return data.ToArray();
    }

    public static Task<HttpResponseMessage> EnviarFotoAsync(
        HttpClient funcionario,
        Guid atendimentoId,
        string tipo,
        byte[] conteudo,
        string mime = "image/jpeg",
        string? versaoAtendimento = null,
        Guid? chave = null,
        string nomeArquivo = "foto-sintetica.jpg")
    {
        var formulario = new MultipartFormDataContent();
        var arquivo = new ByteArrayContent(conteudo);
        arquivo.Headers.ContentType = MediaTypeHeaderValue.Parse(mime);
        formulario.Add(arquivo, "arquivo", nomeArquivo);

        if (versaoAtendimento is not null)
        {
            formulario.Add(new StringContent(versaoAtendimento), "versaoAtendimentoEsperada");
        }

        var requisicao = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/attendances/{atendimentoId}/inspections/{tipo}/photos")
        {
            Content = formulario
        };

        requisicao.Headers.Add("Idempotency-Key", (chave ?? Guid.NewGuid()).ToString());
        return funcionario.SendAsync(requisicao);
    }

    public static async Task<JsonDocument> EnviarFotoConfirmadaAsync(
        HttpClient funcionario,
        Guid atendimentoId,
        string tipo,
        string? versaoAtendimento = null,
        byte[]? conteudo = null,
        string mime = "image/jpeg",
        Guid? chave = null)
    {
        var versao = versaoAtendimento ??
            await Fase4Apoio.VersaoAtendimentoAsync(funcionario, atendimentoId);
        using var resposta = await EnviarFotoAsync(
            funcionario,
            atendimentoId,
            tipo,
            conteudo ?? GerarImagem(),
            mime,
            versao,
            chave);
        Assert.AreEqual(HttpStatusCode.Created, resposta.StatusCode, await resposta.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
    }

    public static async Task<JsonDocument> ListarFotosAsync(
        HttpClient funcionario,
        Guid atendimentoId,
        string tipo,
        HttpStatusCode esperado = HttpStatusCode.OK)
    {
        var resposta = await funcionario.GetAsync(
            $"/api/v1/attendances/{atendimentoId}/inspections/{tipo}/photos");
        Assert.AreEqual(esperado, resposta.StatusCode, await resposta.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
    }

    public static Task<HttpResponseMessage> LerConteudoAsync(
        HttpClient funcionario,
        Guid atendimentoId,
        string tipo,
        Guid fotoId) =>
        funcionario.GetAsync(
            $"/api/v1/attendances/{atendimentoId}/inspections/{tipo}/photos/{fotoId}/content");
}
