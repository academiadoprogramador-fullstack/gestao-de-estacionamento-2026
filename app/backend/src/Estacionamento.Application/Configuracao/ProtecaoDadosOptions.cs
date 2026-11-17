using Microsoft.Extensions.Options;

namespace Estacionamento.Application.Configuracao;

/// <summary>
/// Contrato do mapa de chaves AES versionadas. Nesta fase o mapa é validado e
/// disponibilizado; a cifragem de campos de negócio pertence às fases seguintes.
/// </summary>
public sealed class ProtecaoDadosOptions
{
    public const string Secao = "ProtecaoDados";

    public string VersaoAtiva { get; set; } = string.Empty;

    /// <summary>
    /// Mapa versão → chave AES-256 em Base64 (exatamente 32 bytes por valor).
    /// </summary>
    public Dictionary<string, string> Chaves { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Valida o mapa AES e garante que nenhum material seja reutilizado entre as
/// finalidades de assinatura JWT e de cifragem.
/// </summary>
public sealed class ProtecaoDadosOptionsValidator : IValidateOptions<ProtecaoDadosOptions>
{
    private readonly IOptions<AutenticacaoJwtOptions> _jwt;

    public ProtecaoDadosOptionsValidator(IOptions<AutenticacaoJwtOptions> jwt)
    {
        _jwt = jwt;
    }

    public ValidateOptionsResult Validate(string? name, ProtecaoDadosOptions options)
    {
        var erros = new List<string>();

        if (string.IsNullOrWhiteSpace(options.VersaoAtiva))
        {
            erros.Add("ProtecaoDados.VersaoAtiva é obrigatória.");
        }
        else if (options.Chaves.Count == 0 || !options.Chaves.ContainsKey(options.VersaoAtiva))
        {
            erros.Add("ProtecaoDados.VersaoAtiva não existe em ProtecaoDados.Chaves.");
        }

        if (options.Chaves.Count == 0)
        {
            erros.Add("ProtecaoDados.Chaves deve conter ao menos uma versão.");
        }

        foreach (var versao in options.Chaves.Keys.Order(StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(versao))
            {
                erros.Add("ProtecaoDados.Chaves contém versão vazia.");
                continue;
            }

            if (!ValidadorChaveSimetrica.PossuiFormatoValido(options.Chaves[versao]))
            {
                erros.Add($"ProtecaoDados.Chaves.{versao} deve ser Base64 de exatamente 32 bytes.");
            }
        }

        var chavesJwt = _jwt.Value.Chaves.Values
            .Where(ValidadorChaveSimetrica.PossuiFormatoValido)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (versao, valor) in options.Chaves)
        {
            if (ValidadorChaveSimetrica.PossuiFormatoValido(valor) && chavesJwt.Contains(valor))
            {
                erros.Add($"ProtecaoDados.Chaves.{versao} reutiliza material de Autenticacao.Jwt.Chaves.");
            }
        }

        return erros.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(erros);
    }
}
