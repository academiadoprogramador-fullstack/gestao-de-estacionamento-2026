namespace Estacionamento.Domain.Autenticacao;

/// <summary>
/// Perfis fixos reconhecidos pelo sistema. A autorização efetiva é sempre
/// aplicada no servidor, independentemente da interface.
/// </summary>
public static class Perfis
{
    public const string Administrador = "Administrador";
    public const string Caixa = "Caixa";
    public const string Manobrista = "Manobrista";

    public static readonly IReadOnlyList<string> Todos = [Administrador, Caixa, Manobrista];

    /// <summary>
    /// Perfis sintéticos provisionados no bootstrap de Development, na ordem
    /// fixa de criação.
    /// </summary>
    public static readonly IReadOnlyList<string> Sinteticos = [Administrador, Caixa, Manobrista];
}
