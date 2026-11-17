namespace Estacionamento.Application.Autenticacao;

/// <summary>
/// Dados mínimos do funcionário devolvidos após autenticação. Não inclui senha,
/// hash, documento ou informações financeiras.
/// </summary>
public sealed record FuncionarioSessao(Guid Id, string Nome, string Email, IReadOnlyList<string> Perfis);

/// <summary>
/// Credenciais de login recebidas no corpo JSON.
/// </summary>
public sealed record RequisicaoLogin(string Email, string Senha);

/// <summary>
/// Resultado de uma emissão de sessão. <see cref="Sucesso"/> falso sempre
/// representa falha genérica (credenciais inválidas ou conta bloqueada).
/// </summary>
public sealed record ResultadoLogin(
    bool Sucesso,
    string? TokenAcesso,
    string? Refresh,
    DateTimeOffset? ExpiraAcessoEmUtc,
    DateTimeOffset? ExpiraSessaoEmUtc,
    FuncionarioSessao? Funcionario)
{
    public static readonly ResultadoLogin Falha = new(false, null, null, null, null, null);

    public static ResultadoLogin CriarSucesso(
        string tokenAcesso,
        string refresh,
        DateTimeOffset expiraAcessoEmUtc,
        DateTimeOffset expiraSessaoEmUtc,
        FuncionarioSessao funcionario) =>
        new(true, tokenAcesso, refresh, expiraAcessoEmUtc, expiraSessaoEmUtc, funcionario);
}

/// <summary>
/// Resultado da consulta autenticada da sessão atual.
/// </summary>
public sealed record SessaoAtual(
    Guid SessaoId,
    FuncionarioSessao Funcionario,
    long VersaoAcesso,
    DateTimeOffset CriadaEmUtc,
    DateTimeOffset ExpiraEmUtc);

/// <summary>
/// Porta dos casos de uso de autenticação e sessão.
/// </summary>
public interface IServicoSessao
{
    Task<ResultadoLogin> LoginAsync(RequisicaoLogin requisicao, string? correlacao, CancellationToken cancellationToken = default);

    Task<ResultadoLogin> RenovarAsync(string refresh, string? correlacao, CancellationToken cancellationToken = default);

    Task<SessaoAtual?> ObterSessaoAsync(Guid sessaoId, CancellationToken cancellationToken = default);

    Task<bool> LogoutAsync(Guid sessaoId, string? correlacao, CancellationToken cancellationToken = default);
}
