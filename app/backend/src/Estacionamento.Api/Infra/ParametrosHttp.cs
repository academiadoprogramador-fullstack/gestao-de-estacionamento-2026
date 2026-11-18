using Estacionamento.Application.Comum;

namespace Estacionamento.Api.Infra;

/// <summary>
/// Leitura e validação dos parâmetros técnicos de paginação. Valores inválidos
/// são recusados, sem ajuste silencioso.
/// </summary>
public static class ParametrosHttp
{
    public static (int Pagina, int Tamanho) LerPaginacao(int? page, int? pageSize)
    {
        if (!Paginacao.TentarNormalizar(page, pageSize, out var pagina, out var tamanho))
        {
            throw ExcecaoAplicacao.Validacao(
                "paginacao_invalida",
                $"A página deve ser maior ou igual a 1 e o tamanho deve estar entre 1 e {Paginacao.Maximo}.");
        }

        return (pagina, tamanho);
    }
}
