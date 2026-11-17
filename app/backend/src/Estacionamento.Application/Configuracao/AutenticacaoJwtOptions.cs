using Microsoft.Extensions.Options;

namespace Estacionamento.Application.Configuracao;

/// <summary>
/// Contrato da configuração de autenticação JWT. O identificador da versão
/// ativa e os parâmetros de tempo não são segredos; os valores de
/// <see cref="Chaves"/> pertencem exclusivamente à configuração externa sensível.
/// </summary>
public sealed class AutenticacaoJwtOptions
{
    public const string Secao = "Autenticacao:Jwt";

    public string Emissor { get; set; } = string.Empty;
    public string Audiencia { get; set; } = string.Empty;
    public string VersaoAtiva { get; set; } = string.Empty;
    public int DuracaoAcessoMinutos { get; set; } = 15;
    public int DuracaoSessaoHoras { get; set; } = 8;
    public int ToleranciaRelogioSegundos { get; set; } = 30;
    public int RotacaoChaveDias { get; set; } = 90;

    /// <summary>
    /// Mapa versão → chave simétrica em Base64 (exatamente 32 bytes por valor).
    /// </summary>
    public Dictionary<string, string> Chaves { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Valida emissor, audiência, versão ativa e material Base64 de exatamente
/// 32 bytes de cada versão JWT. Mensagens nunca repetem o valor da chave.
/// </summary>
public sealed class AutenticacaoJwtOptionsValidator : IValidateOptions<AutenticacaoJwtOptions>
{
    public ValidateOptionsResult Validate(string? name, AutenticacaoJwtOptions options)
    {
        var erros = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Emissor))
        {
            erros.Add("Autenticacao.Jwt.Emissor é obrigatório.");
        }

        if (string.IsNullOrWhiteSpace(options.Audiencia))
        {
            erros.Add("Autenticacao.Jwt.Audiencia é obrigatória.");
        }

        if (string.IsNullOrWhiteSpace(options.VersaoAtiva))
        {
            erros.Add("Autenticacao.Jwt.VersaoAtiva é obrigatória.");
        }
        else if (options.Chaves.Count == 0 || !options.Chaves.ContainsKey(options.VersaoAtiva))
        {
            erros.Add("Autenticacao.Jwt.VersaoAtiva não existe em Autenticacao.Jwt.Chaves.");
        }

        if (options.Chaves.Count == 0)
        {
            erros.Add("Autenticacao.Jwt.Chaves deve conter ao menos uma versão.");
        }

        foreach (var versao in options.Chaves.Keys.Order(StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(versao))
            {
                erros.Add("Autenticacao.Jwt.Chaves contém versão vazia.");
                continue;
            }

            if (!ValidadorChaveSimetrica.PossuiFormatoValido(options.Chaves[versao]))
            {
                erros.Add($"Autenticacao.Jwt.Chaves.{versao} deve ser Base64 de exatamente 32 bytes.");
            }
        }

        if (options.DuracaoAcessoMinutos is <= 0 or > 1440)
        {
            erros.Add("Autenticacao.Jwt.DuracaoAcessoMinutos deve estar entre 1 e 1440.");
        }

        if (options.DuracaoSessaoHoras is <= 0 or > 24)
        {
            erros.Add("Autenticacao.Jwt.DuracaoSessaoHoras deve estar entre 1 e 24.");
        }

        if (options.ToleranciaRelogioSegundos is < 0 or > 300)
        {
            erros.Add("Autenticacao.Jwt.ToleranciaRelogioSegundos deve estar entre 0 e 300.");
        }

        if (options.RotacaoChaveDias <= 0)
        {
            erros.Add("Autenticacao.Jwt.RotacaoChaveDias deve ser positivo.");
        }

        return erros.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(erros);
    }
}
