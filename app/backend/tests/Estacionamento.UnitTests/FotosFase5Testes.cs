using System.Text;
using Estacionamento.Application.Comum;
using Estacionamento.Application.Configuracao;
using Estacionamento.Domain.Fotos;
using Estacionamento.Infrastructure.Servicos;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace Estacionamento.UnitTests;

[TestClass]
public sealed class FotosFase5Testes
{
    private static ProcessadorImagensSkia CriarProcessador()
    {
        var opcoes = new ArmazenamentoFotosOptions
        {
            Habilitado = true,
            Limites = new LimitesFotosOptions
            {
                MaximoFotosPorVistoria = 5,
                TamanhoMaximoArquivoMiB = 5,
                MaximoMegapixels = 20,
                MaximoPixelsPorLado = 10_000,
                LadoMaiorSaidaPixels = 2_560,
                QualidadeWebp = 85,
                TimeoutProcessamentoSegundos = 30
            }
        };

        return new ProcessadorImagensSkia(Options.Create(opcoes));
    }

    private static byte[] GerarImagem(
        int largura,
        int altura,
        SKEncodedImageFormat formato = SKEncodedImageFormat.Jpeg,
        int qualidade = 90)
    {
        using var bitmap = new SKBitmap(Math.Max(1, largura), Math.Max(1, altura));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(30, 50, 70));
            using var pincel = new SKPaint { Color = new SKColor(220, 220, 220) };
            canvas.DrawRect(new SKRect(0, 0, Math.Max(1, largura / 2), Math.Max(1, altura / 2)), pincel);
        }

        using var imagem = SKImage.FromBitmap(bitmap);
        using var data = imagem.Encode(formato, qualidade);
        return data.ToArray();
    }

    [TestMethod]
    public void RegraDeJanelaELimiteExplicitaEntradaSaida()
    {
        Assert.IsTrue(RegraFotosVistoria.EntradaAberta(temPedido: false, entregue: false));
        Assert.IsFalse(RegraFotosVistoria.EntradaAberta(temPedido: true, entregue: false));
        Assert.IsFalse(RegraFotosVistoria.EntradaAberta(temPedido: false, entregue: true));
        Assert.IsTrue(RegraFotosVistoria.SaidaAberta(vistoriaRegistrada: true, entregue: false));
        Assert.IsFalse(RegraFotosVistoria.SaidaAberta(vistoriaRegistrada: false, entregue: false));
        Assert.IsFalse(RegraFotosVistoria.SaidaAberta(vistoriaRegistrada: true, entregue: true));
        Assert.IsTrue(RegraFotosVistoria.LimiteAtingido(5));
        Assert.IsFalse(RegraFotosVistoria.LimiteAtingido(4));
        Assert.IsTrue(RegraFotosVistoria.PerfilPodeEnviar(TiposVistoriaFoto.Entrada, ehOperador: true));
        Assert.IsFalse(RegraFotosVistoria.PerfilPodeEnviar(TiposVistoriaFoto.Entrada, ehOperador: false));
        Assert.IsTrue(RegraFotosVistoria.PerfilPodeEnviar(TiposVistoriaFoto.Saida, ehOperador: false));
    }

    [TestMethod]
    public void ValidadorExigeCredenciaisSomenteQuandoHabilitado()
    {
        var validador = new ArmazenamentoFotosOptionsValidator();

        var desabilitado = new ArmazenamentoFotosOptions { Habilitado = false };
        Assert.IsTrue(validador.Validate(null, desabilitado).Succeeded);

        var habilitadoSemCredenciais = new ArmazenamentoFotosOptions
        {
            Habilitado = true,
            R2 = new R2Options { Endpoint = "https://exemplo.invalid", Bucket = "bucket" }
        };
        var falha = validador.Validate(null, habilitadoSemCredenciais);
        Assert.IsFalse(falha.Succeeded);
        Assert.IsTrue(falha.Failures?.Any(mensagem => mensagem.Contains("AccessKeyId", StringComparison.Ordinal)) == true);

        var habilitado = new ArmazenamentoFotosOptions
        {
            Habilitado = true,
            R2 = new R2Options
            {
                Endpoint = "https://exemplo.invalid",
                Bucket = "bucket",
                AccessKeyId = "id",
                SecretAccessKey = "segredo"
            },
            Orcamento = new OrcamentoFotosOptions
            {
                FranquiaBytesMensal = 1000,
                FranquiaOperacoesClasseAMensal = 1000,
                FranquiaOperacoesClasseBMensal = 1000,
                AvisoPercentual = 70,
                BloqueioPercentual = 80
            }
        };
        Assert.IsTrue(validador.Validate(null, habilitado).Succeeded);
    }

    [TestMethod]
    public async Task ProcessadorAceitaJpegPngWebpERegravaWebpSemMetadados()
    {
        var processador = CriarProcessador();

        foreach (var formato in new[]
                 {
                     SKEncodedImageFormat.Jpeg,
                     SKEncodedImageFormat.Png,
                     SKEncodedImageFormat.Webp
                 })
        {
            var original = GerarImagem(320, 200, formato);
            var mime = formato switch
            {
                SKEncodedImageFormat.Png => "image/png",
                SKEncodedImageFormat.Webp => "image/webp",
                _ => "image/jpeg"
            };

            var resultado = await processador.ProcessarAsync(
                new MemoryStream(original),
                mime);

            Assert.AreEqual("image/webp", resultado.Mime);
            Assert.AreEqual(320, resultado.Largura);
            Assert.AreEqual(200, resultado.Altura);
            Assert.AreEqual(32, resultado.HashSha256.Length);
            Assert.AreEqual("RIFF", Encoding.ASCII.GetString(resultado.Conteudo, 0, 4));
            Assert.AreEqual("WEBP", Encoding.ASCII.GetString(resultado.Conteudo, 8, 4));
        }
    }

    [TestMethod]
    public async Task ProcessadorReduzSemAmpliarELimitaLadoMaior()
    {
        var processador = CriarProcessador();

        var grande = await processador.ProcessarAsync(
            new MemoryStream(GerarImagem(4000, 2000, SKEncodedImageFormat.Png, 80)),
            "image/png");
        Assert.AreEqual(2560, grande.Largura);
        Assert.AreEqual(1280, grande.Altura);

        var pequena = await processador.ProcessarAsync(
            new MemoryStream(GerarImagem(100, 80, SKEncodedImageFormat.Jpeg)),
            "image/jpeg");
        Assert.AreEqual(100, pequena.Largura);
        Assert.AreEqual(80, pequena.Altura);
    }

    [TestMethod]
    public async Task ProcessadorAplicaOrientacaoExifSemCopiarMetadados()
    {
        var processador = CriarProcessador();
        var jpeg = GerarImagem(200, 100, SKEncodedImageFormat.Jpeg);
        var comOrientacao = InserirApp1Exif(jpeg, orientacao: 6);

        var resultado = await processador.ProcessarAsync(
            new MemoryStream(comOrientacao),
            "image/jpeg");

        Assert.AreEqual(100, resultado.Largura);
        Assert.AreEqual(200, resultado.Altura);
        var texto = Encoding.ASCII.GetString(resultado.Conteudo);
        Assert.IsFalse(texto.Contains("Exif", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task ProcessadorRemoveMarcadorExifDoJpeg()
    {
        var processador = CriarProcessador();
        var jpeg = GerarImagem(160, 120, SKEncodedImageFormat.Jpeg);
        var comExif = InserirApp1Exif(jpeg);

        var resultado = await processador.ProcessarAsync(
            new MemoryStream(comExif),
            "image/jpeg");

        var texto = Encoding.ASCII.GetString(resultado.Conteudo);
        Assert.IsFalse(texto.Contains("Exif", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(texto.Contains("GPS", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task ProcessadorAceitaConcorrenciaLimitadaSemFilaIlimitada()
    {
        var processador = CriarProcessador();
        var imagem = GerarImagem(1200, 900, SKEncodedImageFormat.Png);

        var tarefas = Enumerable.Range(0, 4)
            .Select(async _ =>
            {
                try
                {
                    return await processador.ProcessarAsync(
                        new MemoryStream(imagem),
                        "image/png");
                }
                catch (ExcecaoAplicacao excecao) when (excecao.Codigo == "photo_processing_busy")
                {
                    return null;
                }
            })
            .ToArray();

        var resultados = await Task.WhenAll(tarefas);
        Assert.IsTrue(resultados.Any(resultado => resultado is not null));
        Assert.IsTrue(resultados.All(resultado => resultado is null || resultado.Mime == "image/webp"));
    }

    [TestMethod]
    public async Task ProcessadorRecusaFormatoCorrompidoAnimadoEDimensaoExcessiva()
    {
        var processador = CriarProcessador();

        await Assert.ThrowsAsync<ExcecaoAplicacao>(() =>
            processador.ProcessarAsync(new MemoryStream(Encoding.UTF8.GetBytes("<svg></svg>")), "image/svg+xml"));

        await Assert.ThrowsAsync<ExcecaoAplicacao>(() =>
            processador.ProcessarAsync(new MemoryStream([0xFF, 0xD8, 0xFF, 0x00]), "image/jpeg"));

        var larga = GerarImagem(10_001, 1, SKEncodedImageFormat.Png);
        await Assert.ThrowsAsync<ExcecaoAplicacao>(() =>
            processador.ProcessarAsync(new MemoryStream(larga), "image/png"));
    }

    private static byte[] InserirApp1Exif(byte[] jpeg, ushort orientacao = 1)
    {
        // APP1 mínimo com TIFF little-endian e uma entrada Orientation. O
        // payload sintético serve apenas para testar orientação e remoção de
        // metadados; não contém dados de pessoa ou veículo.
        var exif = new byte[32];
        exif[0] = (byte)'E';
        exif[1] = (byte)'x';
        exif[2] = (byte)'i';
        exif[3] = (byte)'f';
        exif[4] = 0;
        exif[5] = 0;
        exif[6] = (byte)'I';
        exif[7] = (byte)'I';
        exif[8] = 0x2A;
        exif[9] = 0x00;
        exif[10] = 0x08;
        exif[11] = 0x00;
        exif[12] = 0x00;
        exif[13] = 0x00;
        exif[14] = 0x01;
        exif[15] = 0x00;
        exif[16] = 0x12;
        exif[17] = 0x01;
        exif[18] = 0x03;
        exif[19] = 0x00;
        exif[20] = 0x01;
        exif[21] = 0x00;
        exif[22] = 0x00;
        exif[23] = 0x00;
        exif[24] = (byte)(orientacao & 0xFF);
        exif[25] = (byte)(orientacao >> 8);
        exif[26] = 0x00;
        exif[27] = 0x00;
        exif[28] = 0x00;
        exif[29] = 0x00;
        exif[30] = 0x00;
        exif[31] = 0x00;

        var tamanho = exif.Length + 2;
        using var memoria = new MemoryStream();
        memoria.Write(jpeg, 0, 2);
        memoria.WriteByte(0xFF);
        memoria.WriteByte(0xE1);
        memoria.WriteByte((byte)(tamanho >> 8));
        memoria.WriteByte((byte)(tamanho & 0xFF));
        memoria.Write(exif);
        memoria.Write(jpeg, 2, jpeg.Length - 2);
        return memoria.ToArray();
    }
}
