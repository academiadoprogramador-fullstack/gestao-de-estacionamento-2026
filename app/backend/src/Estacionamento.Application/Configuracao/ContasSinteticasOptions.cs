using System.Net.Mail;
using Estacionamento.Domain.Autenticacao;

namespace Estacionamento.Application.Configuracao;

/// <summary>
/// Conta sintética configurada exclusivamente em User Secrets de Development.
/// Nenhum valor desta classe pode ser registrado em log ou versionado.
/// </summary>
public sealed class ContaSinteticaOptions
{
    public string Email { get; set; } = string.Empty;
    public string Senha { get; set; } = string.Empty;
}

/// <summary>
/// Seção <c>ContasSinteticas</c>: uma conta por perfil fixo.
/// </summary>
public sealed class ContasSinteticasOptions
{
    public const string Secao = "ContasSinteticas";
    public const int TamanhoMinimoSenha = 8;

    public ContaSinteticaOptions? Administrador { get; set; }
    public ContaSinteticaOptions? Caixa { get; set; }
    public ContaSinteticaOptions? Manobrista { get; set; }

    public IReadOnlyList<(string Perfil, ContaSinteticaOptions? Conta)> ObterTodas() =>
    [
        (Perfis.Administrador, Administrador),
        (Perfis.Caixa, Caixa),
        (Perfis.Manobrista, Manobrista)
    ];
}

/// <summary>
/// Resultado de uma validação de contas sem exposição dos valores originais.
/// </summary>
public sealed record ResultadoValidacaoContas(bool Valido, IReadOnlyList<string> Erros)
{
    public static readonly ResultadoValidacaoContas Sucesso = new(true, []);
}

/// <summary>
/// Valida presença, formato de e-mail, senha mínima e unicidade dos e-mails
/// após normalização, antes de qualquer acesso ao banco.
/// </summary>
public sealed class ValidadorContasSinteticas
{
    public ResultadoValidacaoContas Validar(ContasSinteticasOptions? options)
    {
        if (options is null)
        {
            return new(false, ["ContasSinteticas não foi configurada."]);
        }

        var erros = new List<string>();
        var emailsNormalizados = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (perfil, conta) in options.ObterTodas())
        {
            if (conta is null)
            {
                erros.Add($"ContasSinteticas.{perfil} é obrigatória.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(conta.Email))
            {
                erros.Add($"ContasSinteticas.{perfil}.Email é obrigatório.");
            }
            else if (!EmailValido(conta.Email))
            {
                erros.Add($"ContasSinteticas.{perfil}.Email não possui formato aceito.");
            }
            else
            {
                var normalizado = NormalizadorEmail.Normalizar(conta.Email);
                if (emailsNormalizados.TryGetValue(normalizado, out var perfilAnterior))
                {
                    erros.Add($"ContasSinteticas.{perfil} repete o e-mail de {perfilAnterior} após normalização.");
                }
                else
                {
                    emailsNormalizados[normalizado] = perfil;
                }
            }

            if (string.IsNullOrWhiteSpace(conta.Senha))
            {
                erros.Add($"ContasSinteticas.{perfil}.Senha é obrigatória.");
            }
            else if (conta.Senha.Length < ContasSinteticasOptions.TamanhoMinimoSenha)
            {
                erros.Add($"ContasSinteticas.{perfil}.Senha deve ter ao menos {ContasSinteticasOptions.TamanhoMinimoSenha} caracteres.");
            }
        }

        return erros.Count == 0 ? ResultadoValidacaoContas.Sucesso : new(false, erros);
    }

    private static bool EmailValido(string email)
    {
        if (email.Contains(' ', StringComparison.Ordinal))
        {
            return false;
        }

        return MailAddress.TryCreate(email, out var endereco) &&
               string.Equals(endereco.Address, email.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
