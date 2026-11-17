using System.Security.Cryptography;
using System.Text;

namespace Estacionamento.Domain.Autenticacao;

/// <summary>
/// Política pura de duração de sessão, refresh e ator opaco de auditoria.
/// </summary>
public static class PoliticaSessao
{
    /// <summary>Duração absoluta da sessão a partir do login.</summary>
    public static readonly TimeSpan DuracaoAbsolutaPadrao = TimeSpan.FromHours(8);

    /// <summary>Duração do token de acesso.</summary>
    public static readonly TimeSpan DuracaoAcessoPadrao = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Calcula o fim absoluto da sessão, sem extensão deslizante.
    /// </summary>
    public static DateTimeOffset CalcularFimSessao(DateTimeOffset inicio, TimeSpan duracaoAbsoluta) =>
        inicio.Add(duracaoAbsoluta);

    /// <summary>
    /// Calcula a validade de um refresh: nunca ultrapassa o fim absoluto da sessão.
    /// </summary>
    public static DateTimeOffset CalcularFimRefresh(DateTimeOffset agora, DateTimeOffset fimSessao) =>
        agora >= fimSessao ? fimSessao : fimSessao;

    /// <summary>
    /// Verifica se a sessão ainda pode ser renovada no instante informado.
    /// </summary>
    public static bool SessaoRenovavel(DateTimeOffset agora, DateTimeOffset fimSessao, bool revogada) =>
        !revogada && agora < fimSessao;

    /// <summary>
    /// Gera um ator opaco determinístico (SHA-256 em hexadecimal) sem expor o
    /// e-mail ou identificador original em auditoria.
    /// </summary>
    public static string GerarAtorOpaco(string identificador)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identificador));
        return Convert.ToHexStringLower(hash);
    }
}
