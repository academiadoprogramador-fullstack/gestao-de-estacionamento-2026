namespace Estacionamento.Api.Infra;

/// <summary>
/// Nome estável da política CORS configurada para as origens exatas da SPA.
/// </summary>
public static class PoliticaCors
{
    public const string Nome = "Spa";
}

/// <summary>
/// Exceção controlada lançada quando a configuração ou o bootstrap impede a
/// API de iniciar. Não contém valores sensíveis.
/// </summary>
public sealed class InicializacaoException : Exception
{
    public InicializacaoException(string mensagem, Exception interna)
        : base(mensagem, interna)
    {
    }
}
