namespace Estacionamento.Application.Comum;

/// <summary>
/// Paginação padrão de 20 e máxima de 100. Valores inválidos são recusados, não
/// ajustados silenciosamente.
/// </summary>
public static class Paginacao
{
    public const int Padrao = 20;
    public const int Maximo = 100;

    public static bool TentarNormalizar(
        int? pagina,
        int? tamanho,
        out int paginaNormalizada,
        out int tamanhoNormalizado)
    {
        paginaNormalizada = pagina ?? 1;
        tamanhoNormalizado = tamanho ?? Padrao;

        return paginaNormalizada >= 1 && tamanhoNormalizado is >= 1 and <= Maximo;
    }
}

/// <summary>Página de resultados com total de itens do filtro.</summary>
public sealed record Pagina<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int Total);
