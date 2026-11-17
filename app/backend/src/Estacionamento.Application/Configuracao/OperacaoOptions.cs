using Microsoft.Extensions.Options;

namespace Estacionamento.Application.Configuracao;

/// <summary>
/// Parâmetros operacionais não sensíveis.
/// </summary>
public sealed class OperacaoOptions
{
    public const string Secao = "Operacao";

    public int IntervaloPollingSegundos { get; set; } = 30;
}

public sealed class OperacaoOptionsValidator : IValidateOptions<OperacaoOptions>
{
    public ValidateOptionsResult Validate(string? name, OperacaoOptions options)
    {
        return options.IntervaloPollingSegundos is > 0 and <= 3600
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Operacao.IntervaloPollingSegundos deve estar entre 1 e 3600.");
    }
}
