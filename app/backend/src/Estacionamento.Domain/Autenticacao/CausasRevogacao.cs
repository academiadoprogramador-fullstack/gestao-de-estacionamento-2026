namespace Estacionamento.Domain.Autenticacao;

/// <summary>
/// Motivos registrados ao revogar uma sessão ou família de refresh.
/// </summary>
public static class CausasRevogacao
{
    public const string Logout = "logout";
    public const string ContaDesativada = "conta_desativada";
    public const string AlteracaoAcesso = "alteracao_acesso";
    public const string ReusoRefresh = "reuso_refresh";
    public const string Expiracao = "expiracao";
    public const string CredenciaisInvalidas = "credenciais_invalidas";
}
