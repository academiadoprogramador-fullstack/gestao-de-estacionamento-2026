using Estacionamento.Application.Comum;

namespace Estacionamento.Api.Infra;

/// <summary>
/// Leitura e validação dos parâmetros técnicos de paginação e do cabeçalho de
/// idempotência. Valores inválidos são recusados, sem ajuste silencioso.
/// </summary>
public static class ParametrosHttp
{
    public const string CabecalhoIdempotencia = "Idempotency-Key";

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

    /// <summary>
    /// Lê o UUID obrigatório do cabeçalho <c>Idempotency-Key</c>. O valor nunca
    /// é fornecido pelo corpo da requisição.
    /// </summary>
    public static Guid LerChaveIdempotencia(HttpRequest requisicao)
    {
        if (!requisicao.Headers.TryGetValue(CabecalhoIdempotencia, out var valores) ||
            !Guid.TryParse(valores.ToString(), out var chave) ||
            chave == Guid.Empty)
        {
            throw ExcecaoAplicacao.Validacao(
                "idempotency_key_invalida",
                "O cabeçalho Idempotency-Key é obrigatório no formato UUID.");
        }

        return chave;
    }
}
