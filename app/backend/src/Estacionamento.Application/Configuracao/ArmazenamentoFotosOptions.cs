using Microsoft.Extensions.Options;

namespace Estacionamento.Application.Configuracao;

/// <summary>
/// Configuração de fotos das vistorias. Endpoint, região, bucket e limites são
/// parâmetros não sensíveis; credenciais S3 vêm exclusivamente de User Secrets
/// ou App Settings e nunca são impressas.
/// </summary>
public sealed class ArmazenamentoFotosOptions
{
    public const string Secao = "ArmazenamentoFotos";

    public bool Habilitado { get; set; }
    public R2Options R2 { get; set; } = new();
    public LimitesFotosOptions Limites { get; set; } = new();
    public OrcamentoFotosOptions Orcamento { get; set; } = new();
    public int RetencaoDias { get; set; } = 90;
    public int DiasParaReconciliarOrfao { get; set; } = 1;
}

/// <summary>Parâmetros do adapter S3/R2. As credenciais são segredos externos.</summary>
public sealed class R2Options
{
    public string Endpoint { get; set; } = string.Empty;
    public string RegiaoAssinatura { get; set; } = "auto";
    public string Bucket { get; set; } = string.Empty;
    public string AccessKeyId { get; set; } = string.Empty;
    public string SecretAccessKey { get; set; } = string.Empty;
}

/// <summary>Limites de aceitação, normalização e processamento.</summary>
public sealed class LimitesFotosOptions
{
    public int MaximoFotosPorVistoria { get; set; } = 5;
    public int TamanhoMaximoArquivoMiB { get; set; } = 5;
    public int MaximoMegapixels { get; set; } = 20;
    public int MaximoPixelsPorLado { get; set; } = 10_000;
    public int LadoMaiorSaidaPixels { get; set; } = 2_560;
    public int QualidadeWebp { get; set; } = 85;
    public int TimeoutProcessamentoSegundos { get; set; } = 30;
}

/// <summary>
/// Franquias mensais compartilhadas. O serviço mantém um contador local e
/// aceita linhas de reconciliação para consumo de outros ambientes/cópias.
/// </summary>
public sealed class OrcamentoFotosOptions
{
    public long FranquiaBytesMensal { get; set; } = 10L * 1024 * 1024 * 1024;
    public long FranquiaOperacoesClasseAMensal { get; set; } = 1_000_000;
    public long FranquiaOperacoesClasseBMensal { get; set; } = 10_000_000;
    public int AvisoPercentual { get; set; } = 70;
    public int BloqueioPercentual { get; set; } = 80;
}

/// <summary>
/// Valida os parâmetros de fotos antes de a API aceitar tráfego. Quando
/// habilitado, exige endpoint HTTPS, bucket e credenciais presentes, sem
/// repetir valores sensíveis na mensagem.
/// </summary>
public sealed class ArmazenamentoFotosOptionsValidator : IValidateOptions<ArmazenamentoFotosOptions>
{
    public ValidateOptionsResult Validate(string? name, ArmazenamentoFotosOptions options)
    {
        var erros = new List<string>();

        if (options.Habilitado)
        {
            if (!Uri.TryCreate(options.R2.Endpoint, UriKind.Absolute, out var endpoint) ||
                !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
            {
                erros.Add("ArmazenamentoFotos:R2:Endpoint deve ser uma URL HTTPS absoluta.");
            }

            if (string.IsNullOrWhiteSpace(options.R2.Bucket))
            {
                erros.Add("ArmazenamentoFotos:R2:Bucket é obrigatório quando o armazenamento está habilitado.");
            }

            if (string.IsNullOrWhiteSpace(options.R2.RegiaoAssinatura))
            {
                erros.Add("ArmazenamentoFotos:R2:RegiaoAssinatura é obrigatória.");
            }

            if (string.IsNullOrWhiteSpace(options.R2.AccessKeyId) ||
                string.IsNullOrWhiteSpace(options.R2.SecretAccessKey))
            {
                erros.Add("ArmazenamentoFotos:R2:AccessKeyId e SecretAccessKey são obrigatórios quando habilitado.");
            }
        }

        if (options.Limites.MaximoFotosPorVistoria is < 1 or > 5)
        {
            erros.Add("ArmazenamentoFotos:Limites:MaximoFotosPorVistoria deve estar entre 1 e 5.");
        }

        if (options.Limites.TamanhoMaximoArquivoMiB is < 1 or > 5)
        {
            erros.Add("ArmazenamentoFotos:Limites:TamanhoMaximoArquivoMiB deve estar entre 1 e 5.");
        }

        if (options.Limites.MaximoMegapixels is < 1 or > 20)
        {
            erros.Add("ArmazenamentoFotos:Limites:MaximoMegapixels deve estar entre 1 e 20.");
        }

        if (options.Limites.MaximoPixelsPorLado is < 1 or > 10_000)
        {
            erros.Add("ArmazenamentoFotos:Limites:MaximoPixelsPorLado deve estar entre 1 e 10.000.");
        }

        if (options.Limites.LadoMaiorSaidaPixels is < 1 or > 2_560)
        {
            erros.Add("ArmazenamentoFotos:Limites:LadoMaiorSaidaPixels deve estar entre 1 e 2.560.");
        }

        if (options.Limites.QualidadeWebp is < 1 or > 100)
        {
            erros.Add("ArmazenamentoFotos:Limites:QualidadeWebp deve estar entre 1 e 100.");
        }

        if (options.Limites.TimeoutProcessamentoSegundos is < 1 or > 120)
        {
            erros.Add("ArmazenamentoFotos:Limites:TimeoutProcessamentoSegundos deve estar entre 1 e 120.");
        }

        if (options.Orcamento.FranquiaBytesMensal <= 0 ||
            options.Orcamento.FranquiaOperacoesClasseAMensal <= 0 ||
            options.Orcamento.FranquiaOperacoesClasseBMensal <= 0)
        {
            erros.Add("ArmazenamentoFotos:Orcamento exige franquias positivas para bytes e Classes A/B.");
        }

        if (options.Orcamento.AvisoPercentual is < 1 or > 100 ||
            options.Orcamento.BloqueioPercentual is < 1 or > 100 ||
            options.Orcamento.AvisoPercentual >= options.Orcamento.BloqueioPercentual)
        {
            erros.Add("ArmazenamentoFotos:Orcamento exige aviso menor que bloqueio, ambos entre 1 e 100.");
        }

        if (options.RetencaoDias <= 0 || options.DiasParaReconciliarOrfao <= 0)
        {
            erros.Add("ArmazenamentoFotos:RetencaoDias e DiasParaReconciliarOrfao devem ser positivos.");
        }

        return erros.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(erros);
    }
}
