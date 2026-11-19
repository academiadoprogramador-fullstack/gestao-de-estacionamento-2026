using Estacionamento.Application.Atendimentos;
using Estacionamento.Application.Comum;

namespace Estacionamento.Application.Caixa;

/// <summary>Acerto integral: exatamente o saldo exigível positivo.</summary>
public sealed record RequisicaoPagamento(
    decimal Valor,
    string? Forma,
    int RevisaoEsperada);

/// <summary>Desconto administrativo com justificativa obrigatória.</summary>
public sealed record RequisicaoDesconto(
    decimal Valor,
    string? Justificativa,
    int RevisaoEsperada);

/// <summary>
/// Estorno integral de um pagamento ainda não estornado. O valor é obrigatório
/// e precisa coincidir com o pagamento original.
/// </summary>
public sealed record RequisicaoEstorno(
    Guid PagamentoId,
    decimal Valor,
    string? Justificativa,
    int RevisaoEsperada);

/// <summary>Concessão de autorização documental ou financeira.</summary>
public sealed record RequisicaoConcessaoAutorizacao(
    string? Tipo,
    string? Justificativa,
    byte[] VersaoEsperada);

/// <summary>Revogação de autorização com justificativa obrigatória.</summary>
public sealed record RequisicaoRevogacaoAutorizacao(string? Justificativa);

/// <summary>Resultado mínimo de um lançamento financeiro idempotente.</summary>
public sealed record ResultadoLancamento(
    Guid PedidoId,
    Guid AtendimentoId,
    Guid LancamentoId,
    string Tipo,
    decimal Valor,
    int RevisaoRazao,
    decimal Saldo,
    string Location);

/// <summary>Resultado mínimo da concessão de autorização.</summary>
public sealed record ResultadoAutorizacao(
    Guid PedidoId,
    Guid AutorizacaoId,
    string Tipo,
    string Vigencia,
    int RevisaoReferencia,
    DateTimeOffset ConcedidaEmUtc,
    string Location);

/// <summary>Resultado mínimo da revogação de autorização.</summary>
public sealed record ResultadoRevogacao(
    Guid PedidoId,
    Guid AutorizacaoId,
    Guid RevogacaoId,
    DateTimeOffset RevogadaEmUtc);

/// <summary>Comandos financeiros serializados pela razão do atendimento.</summary>
public interface IServicoCaixa
{
    Task<ResultadoLancamento> RegistrarPagamentoAsync(
        AtorAtual ator,
        Guid pedidoId,
        RequisicaoPagamento requisicao,
        Guid chaveIdempotente,
        string? correlacao,
        CancellationToken cancellationToken = default);

    Task<ResultadoLancamento> RegistrarDescontoAsync(
        AtorAtual ator,
        Guid pedidoId,
        RequisicaoDesconto requisicao,
        Guid chaveIdempotente,
        string? correlacao,
        CancellationToken cancellationToken = default);

    Task<ResultadoLancamento> RegistrarEstornoAsync(
        AtorAtual ator,
        Guid pedidoId,
        RequisicaoEstorno requisicao,
        Guid chaveIdempotente,
        string? correlacao,
        CancellationToken cancellationToken = default);

    Task<LeituraSensivel> LerJustificativaLancamentoAsync(
        AtorAtual ator,
        Guid pedidoId,
        Guid lancamentoId,
        string? correlacao,
        CancellationToken cancellationToken = default);
}

/// <summary>Concessão, revogação e leitura auditada de autorizações.</summary>
public interface IServicoAutorizacoes
{
    Task<ResultadoAutorizacao> ConcederAsync(
        AtorAtual ator,
        Guid pedidoId,
        RequisicaoConcessaoAutorizacao requisicao,
        Guid chaveIdempotente,
        string? correlacao,
        CancellationToken cancellationToken = default);

    Task<ResultadoRevogacao> RevogarAsync(
        AtorAtual ator,
        Guid pedidoId,
        Guid autorizacaoId,
        RequisicaoRevogacaoAutorizacao requisicao,
        Guid chaveIdempotente,
        string? correlacao,
        CancellationToken cancellationToken = default);

    Task<LeituraSensivel> LerJustificativaAsync(
        AtorAtual ator,
        Guid pedidoId,
        Guid autorizacaoId,
        string? correlacao,
        CancellationToken cancellationToken = default);
}
