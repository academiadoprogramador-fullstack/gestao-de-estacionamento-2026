namespace Estacionamento.Domain.Autenticacao;

/// <summary>
/// Tipos de evento de auditoria de autenticação. Os valores persistidos são
/// estáveis e usados em relatórios e testes.
/// </summary>
public static class TiposEventoAuditoria
{
    public const string LoginSucesso = "login_sucesso";
    public const string LoginFalha = "login_falha";
    public const string LoginBloqueado = "login_bloqueado";
    public const string LoginLimitado = "login_limitado";
    public const string Logout = "logout";
    public const string RevogacaoSessao = "revogacao_sessao";
    public const string RenovacaoSessao = "renovacao_sessao";
    public const string ReusoRefresh = "reuso_refresh";
    public const string ProvisionamentoConta = "provisionamento_conta";
}
