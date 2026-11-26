using System.Security.Cryptography;
using Estacionamento.Application.Comum;
using Estacionamento.Application.Configuracao;
using Estacionamento.Application.Fotos;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace Estacionamento.Infrastructure.Servicos;

/// <summary>
/// Decodifica, orienta, reduz e regrava a imagem em WebP sem copiar EXIF/GPS.
/// Um único processamento roda por instância; excedentes recebem recusa
/// temporária em vez de formar fila ilimitada. Limites são checados antes de
/// alocar o bitmap completo.
/// </summary>
public sealed class ProcessadorImagensSkia : IProcessadorImagens
{
    private static readonly HashSet<string> MimesAceitos = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp"
    };

    private readonly SemaphoreSlim _bloqueio = new(1, 1);
    private readonly ArmazenamentoFotosOptions _opcoes;

    public ProcessadorImagensSkia(IOptions<ArmazenamentoFotosOptions> opcoes)
    {
        _opcoes = opcoes.Value;
    }

    public async Task<ImagemProcessada> ProcessarAsync(
        Stream conteudo,
        string? tipoConteudoDeclarado,
        CancellationToken cancellationToken = default)
    {
        var limiteBytes = (long)_opcoes.Limites.TamanhoMaximoArquivoMiB * 1024 * 1024;

        if (!MimesAceitos.Contains(tipoConteudoDeclarado ?? string.Empty))
        {
            throw ExcecaoAplicacao.FormatoNaoAceito(
                "photo_unsupported_format",
                "Envie uma imagem JPEG, PNG ou WebP estática.");
        }

        var bytes = await LerComLimiteAsync(conteudo, limiteBytes, cancellationToken);
        var formato = DetectarFormato(bytes);
        if (formato is null ||
            !string.Equals(formato, tipoConteudoDeclarado, StringComparison.OrdinalIgnoreCase))
        {
            throw ExcecaoAplicacao.FormatoNaoAceito(
                "photo_unsupported_format",
                "O conteúdo do arquivo não corresponde a JPEG, PNG ou WebP estático.");
        }

        if (!await _bloqueio.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken))
        {
            throw ExcecaoAplicacao.Indisponivel(
                "photo_processing_busy",
                "O processamento de imagens está ocupado. Tente novamente em instantes.");
        }

        try
        {
            var tarefa = Task.Run(() => Processar(bytes), cancellationToken);
            try
            {
                return await tarefa.WaitAsync(
                    TimeSpan.FromSeconds(_opcoes.Limites.TimeoutProcessamentoSegundos),
                    cancellationToken);
            }
            catch (TimeoutException)
            {
                throw ExcecaoAplicacao.Indisponivel(
                    "photo_processing_timeout",
                    "O processamento da imagem excedeu o tempo limite. A vistoria textual continua disponível.");
            }
        }
        finally
        {
            _bloqueio.Release();
        }
    }

    private ImagemProcessada Processar(byte[] bytes)
    {
        using var memoria = new SKMemoryStream(bytes);
        using var codec = SKCodec.Create(memoria)
            ?? throw ExcecaoAplicacao.FormatoNaoAceito(
                "photo_corrupt",
                "A imagem está corrompida ou em formato não aceito.");

        if (codec.FrameCount > 1)
        {
            throw ExcecaoAplicacao.FormatoNaoAceito(
                "photo_animated",
                "Animações não são aceitas; envie uma imagem estática.");
        }

        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0)
        {
            throw ExcecaoAplicacao.FormatoNaoAceito("photo_corrupt", "A imagem não possui dimensões válidas.");
        }

        var megapixels = (long)info.Width * info.Height;
        if (megapixels > (long)_opcoes.Limites.MaximoMegapixels * 1_000_000 ||
            info.Width > _opcoes.Limites.MaximoPixelsPorLado ||
            info.Height > _opcoes.Limites.MaximoPixelsPorLado)
        {
            throw ExcecaoAplicacao.Validacao(
                "photo_dimensions_exceeded",
                $"A imagem excede {_opcoes.Limites.MaximoMegapixels} MP ou {_opcoes.Limites.MaximoPixelsPorLado} px por lado.");
        }

        using var original = new SKBitmap(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var resultado = codec.GetPixels(info, original.GetPixels());
        if (resultado != SKCodecResult.Success)
        {
            throw ExcecaoAplicacao.FormatoNaoAceito("photo_corrupt", "A imagem não pôde ser decodificada.");
        }

        using var orientada = AplicarOrientacao(original, codec.EncodedOrigin);
        using var redimensionada = Redimensionar(orientada);
        using var imagem = SKImage.FromBitmap(redimensionada);
        using var data = imagem.Encode(SKEncodedImageFormat.Webp, _opcoes.Limites.QualidadeWebp)
            ?? throw ExcecaoAplicacao.Indisponivel(
                "photo_encode_failed",
                "Não foi possível normalizar a imagem. A vistoria textual continua disponível.");

        var conteudo = data.ToArray();
        return new ImagemProcessada(
            conteudo,
            "image/webp",
            redimensionada.Width,
            redimensionada.Height,
            SHA256.HashData(conteudo));
    }

    private SKBitmap Redimensionar(SKBitmap origem)
    {
        var maiorLado = Math.Max(origem.Width, origem.Height);
        var limite = _opcoes.Limites.LadoMaiorSaidaPixels;

        if (maiorLado <= limite)
        {
            return origem.Copy();
        }

        var fator = (double)limite / maiorLado;
        var largura = Math.Max(1, (int)Math.Round(origem.Width * fator));
        var altura = Math.Max(1, (int)Math.Round(origem.Height * fator));
        var destino = new SKBitmap(new SKImageInfo(largura, altura, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(destino);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(
            origem,
            new SKRect(0, 0, largura, altura),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
            new SKPaint { IsAntialias = true });
        return destino;
    }

    /// <summary>
    /// Aplica a orientação declarada pelo codec sem copiar metadados. O canvas
    /// recebe a transformação e o resultado já sai com os pixels na posição
    /// visual correta.
    /// </summary>
    private static SKBitmap AplicarOrientacao(SKBitmap origem, SKEncodedOrigin origemExif)
    {
        if (origemExif is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default)
        {
            return origem.Copy();
        }

        var trocar = origemExif is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

        var destino = new SKBitmap(new SKImageInfo(
            trocar ? origem.Height : origem.Width,
            trocar ? origem.Width : origem.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul));

        using var canvas = new SKCanvas(destino);
        canvas.Clear(SKColors.Transparent);

        switch (origemExif)
        {
            case SKEncodedOrigin.TopRight:
                canvas.Scale(-1, 1, destino.Width / 2f, 0);
                break;
            case SKEncodedOrigin.BottomRight:
                canvas.RotateDegrees(180, destino.Width / 2f, destino.Height / 2f);
                break;
            case SKEncodedOrigin.BottomLeft:
                canvas.Scale(1, -1, 0, destino.Height / 2f);
                break;
            case SKEncodedOrigin.LeftTop:
                canvas.RotateDegrees(90);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.RightTop:
                canvas.Translate(destino.Width, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightBottom:
                canvas.Translate(destino.Width, destino.Height);
                canvas.RotateDegrees(-90);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.LeftBottom:
                canvas.Translate(0, destino.Height);
                canvas.RotateDegrees(-90);
                break;
        }

        canvas.DrawBitmap(
            origem,
            0,
            0,
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
            new SKPaint { IsAntialias = true });
        return destino;
    }

    private static async Task<byte[]> LerComLimiteAsync(
        Stream conteudo,
        long limite,
        CancellationToken cancellationToken)
    {
        using var memoria = new MemoryStream();
        var buffer = new byte[81920];
        long total = 0;

        while (true)
        {
            var lidos = await conteudo.ReadAsync(buffer, cancellationToken);
            if (lidos == 0)
            {
                break;
            }

            total += lidos;
            if (total > limite)
            {
                throw ExcecaoAplicacao.ArquivoMuitoGrande(
                    "photo_too_large",
                    "A foto excede o limite de 5 MiB por arquivo.");
            }

            await memoria.WriteAsync(buffer.AsMemory(0, lidos), cancellationToken);
        }

        if (total == 0)
        {
            throw ExcecaoAplicacao.FormatoNaoAceito("photo_empty", "O arquivo enviado está vazio.");
        }

        return memoria.ToArray();
    }

    private static string? DetectarFormato(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (bytes.Length >= 8 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
            bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
        {
            return "image/png";
        }

        if (bytes.Length >= 12 &&
            bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F' &&
            bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P')
        {
            return "image/webp";
        }

        return null;
    }
}
