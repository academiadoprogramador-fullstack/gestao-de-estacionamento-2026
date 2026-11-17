using Estacionamento.Domain.Autenticacao;

namespace Estacionamento.Api.Autenticacao;

/// <summary>
/// Nomes estáveis de políticas de autorização e limitação usados por
/// controllers, testes e documentação.
/// </summary>
public static class PoliticasAutorizacao
{
    public const string Funcionario = "Funcionario";
    public const string Administrador = "Administrador";
}

public static class PoliticasRateLimit
{
    public const string Login = "login";
}

/// <summary>
/// Registra as políticas de autorização por perfil fixo. Autorizações são
/// aplicadas no servidor independentemente da interface.
/// </summary>
public static class ConfiguracaoAutorizacao
{
    public static IServiceCollection AdicionarAutorizacaoPorPerfil(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(PoliticasAutorizacao.Funcionario, politica =>
                politica.RequireRole(Perfis.Todos.ToArray()));

            options.AddPolicy(PoliticasAutorizacao.Administrador, politica =>
                politica.RequireRole(Perfis.Administrador));
        });

        return services;
    }
}
