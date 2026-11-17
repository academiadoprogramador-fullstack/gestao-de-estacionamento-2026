namespace Estacionamento.Application.Autenticacao;

/// <summary>
/// Token de acesso emitido pela API.
/// </summary>
public sealed record TokenJwt(string Valor, DateTimeOffset ExpiraEmUtc);

/// <summary>
/// Porta de emissão de JWT HS256 com chave versionada.
/// </summary>
public interface IGeradorTokenJwt
{
    TokenJwt Emitir(
        Guid usuarioId,
        Guid sessaoId,
        long versaoAcesso,
        IEnumerable<string> perfis,
        DateTimeOffset? limiteAbsoluto = null);
}

/// <summary>
/// Porta de geração e hash de refresh opacos. O valor puro só existe em memória
/// do cliente; o SQL guarda apenas o hash SHA-256.
/// </summary>
public interface IGeradorRefresh
{
    string GerarTokenOpaco();

    string CalcularHash(string tokenOpaco);
}
