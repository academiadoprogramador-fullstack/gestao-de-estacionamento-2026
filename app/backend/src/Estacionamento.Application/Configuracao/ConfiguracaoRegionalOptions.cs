using System.Globalization;
using Microsoft.Extensions.Options;

namespace Estacionamento.Application.Configuracao;

/// <summary>
/// Configuração regional não sensível aplicada à apresentação e às consultas.
/// </summary>
public sealed class ConfiguracaoRegionalOptions
{
    public const string Secao = "ConfiguracaoRegional";

    public string Cultura { get; set; } = "pt-BR";
    public string FusoHorario { get; set; } = "America/Sao_Paulo";
    public string Moeda { get; set; } = "BRL";
}

/// <summary>
/// Valida a configuração regional sem depender do ambiente de execução.
/// </summary>
public sealed class ConfiguracaoRegionalOptionsValidator : IValidateOptions<ConfiguracaoRegionalOptions>
{
    public ValidateOptionsResult Validate(string? name, ConfiguracaoRegionalOptions options)
    {
        var erros = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Cultura))
        {
            erros.Add("ConfiguracaoRegional.Cultura é obrigatória.");
        }
        else
        {
            try
            {
                _ = CultureInfo.GetCultureInfo(options.Cultura);
            }
            catch (CultureNotFoundException)
            {
                erros.Add("ConfiguracaoRegional.Cultura não é uma cultura válida.");
            }
        }

        if (string.IsNullOrWhiteSpace(options.FusoHorario))
        {
            erros.Add("ConfiguracaoRegional.FusoHorario é obrigatório.");
        }
        else
        {
            try
            {
                _ = TimeZoneInfo.FindSystemTimeZoneById(options.FusoHorario);
            }
            catch (TimeZoneNotFoundException)
            {
                erros.Add("ConfiguracaoRegional.FusoHorario não é um fuso válido.");
            }
        }

        if (string.IsNullOrWhiteSpace(options.Moeda) || options.Moeda.Length != 3)
        {
            erros.Add("ConfiguracaoRegional.Moeda deve ter três letras.");
        }

        return erros.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(erros);
    }
}
