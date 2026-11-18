using Estacionamento.Domain.Autenticacao;

namespace Estacionamento.Application.Comum;

/// <summary>
/// Ator autenticado resolvido no servidor a partir do JWT e da sessão. Nunca é
/// fornecido pelo corpo da requisição.
/// </summary>
public sealed record AtorAtual(Guid Id, IReadOnlyList<string> Perfis)
{
    public bool TemPerfil(params string[] perfis) =>
        perfis.Any(perfil => Perfis.Contains(perfil, StringComparer.Ordinal));

    public bool EhAdministrador => TemPerfil(PerfisFixos.Administrador);

    public bool EhOperador => TemPerfil(PerfisFixos.Administrador, PerfisFixos.Caixa);
}

/// <summary>
/// Referência local aos identificadores de perfil fixos, evitando que a camada
/// de aplicação dependa de constantes espalhadas.
/// </summary>
public static class PerfisFixos
{
    public const string Administrador = Perfis.Administrador;
    public const string Caixa = Perfis.Caixa;
    public const string Manobrista = Perfis.Manobrista;
}
