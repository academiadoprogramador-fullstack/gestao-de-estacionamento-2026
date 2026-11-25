using Estacionamento.Application.Comum;
using Estacionamento.Application.Entrega;

namespace Estacionamento.Application.Retiradas;

/// <summary>
/// Pedido de retirada aceito do cliente. Não existe propriedade para documento,
/// foto, novo número, horário, ator, tarifa ou total; propriedades
/// desconhecidas são rejeitadas pela configuração estrita do serializador.
/// </summary>
public sealed record RequisicaoRegistrarPedidoRetirada(
    bool TitularPresente,
    string? Conferencia);

/// <summary>Resultado mínimo do pedido, usado também no replay idempotente.</summary>
public sealed record ResultadoPedidoRetirada(
    Guid PedidoId,
    Guid AtendimentoId,
    string Identificador,
    string Estado,
    DateTimeOffset SolicitadoEmUtc,
    int Diarias,
    decimal TotalOriginal,
    string Location);

/// <summary>Início de busca com a versão esperada do pedido.</summary>
public sealed record RequisicaoIniciarBusca(byte[] VersaoEsperada);

/// <summary>Projeção operacional da fila, visível aos três perfis.</summary>
public sealed record RetiradaFilaOperacional(
    Guid Id,
    Guid AtendimentoId,
    string Identificador,
    string Placa,
    string Modelo,
    string VagaCodigo,
    string Estado,
    DateTimeOffset EntradaUtc,
    DateTimeOffset SolicitadoEmUtc,
    DateTimeOffset? BuscaIniciadaEmUtc,
    string? ResponsavelBusca,
    byte[] VersaoPedido,
    bool VistoriaSaidaRegistrada = false,
    ResumoEntrega? Entrega = null);

/// <summary>
/// Projeção financeira da fila, restrita a Caixa/Administrador. O Manobrista
/// recebe outro tipo, sem valores ou situação de autorizações.
/// </summary>
public sealed record RetiradaFilaFinanceira(
    Guid Id,
    Guid AtendimentoId,
    string Identificador,
    string Placa,
    string Modelo,
    string VagaCodigo,
    string Estado,
    DateTimeOffset EntradaUtc,
    DateTimeOffset SolicitadoEmUtc,
    DateTimeOffset? BuscaIniciadaEmUtc,
    string? ResponsavelBusca,
    byte[] VersaoPedido,
    decimal TotalOriginal,
    decimal PagoLiquido,
    decimal Saldo,
    string AutorizacaoDocumental,
    string AutorizacaoFinanceira,
    bool VistoriaSaidaRegistrada = false,
    ResumoEntrega? Entrega = null);

/// <summary>Lançamento financeiro resumido, sem justificativa.</summary>
public sealed record LancamentoResumo(
    Guid Id,
    string Tipo,
    decimal Valor,
    string? FormaPagamento,
    Guid? PagamentoOriginalId,
    string Ator,
    DateTimeOffset OcorridoEmUtc,
    bool TemJustificativa);

/// <summary>Situação derivada de autorização, sem texto de justificativa.</summary>
public sealed record AutorizacaoResumo(
    string Tipo,
    Guid? Id,
    string Vigencia,
    int? RevisaoReferencia,
    string? ConcedidaPor,
    DateTimeOffset? ConcedidaEmUtc,
    bool TemJustificativa,
    DateTimeOffset? ConsumidaEmUtc = null);

/// <summary>Detalhe operacional do pedido, sem conferência ou finanças.</summary>
public sealed record RetiradaDetalheOperacional(
    Guid Id,
    Guid AtendimentoId,
    string Identificador,
    string Placa,
    string Modelo,
    string VagaCodigo,
    string Estado,
    DateTimeOffset EntradaUtc,
    DateTimeOffset SolicitadoEmUtc,
    DateTimeOffset? BuscaIniciadaEmUtc,
    string? ResponsavelBusca,
    byte[] VersaoPedido,
    bool VistoriaSaidaRegistrada = false,
    ResumoEntrega? Entrega = null);

/// <summary>
/// Detalhe financeiro do pedido, restrito a Caixa/Administrador. Inclui
/// conferência, cobrança fixada, lançamentos, saldo, revisão e autorizações,
/// sem o texto das justificativas.
/// </summary>
public sealed record RetiradaDetalheFinanceira(
    Guid Id,
    Guid AtendimentoId,
    string Identificador,
    string Placa,
    string Modelo,
    string VagaCodigo,
    string Estado,
    DateTimeOffset EntradaUtc,
    DateTimeOffset SolicitadoEmUtc,
    DateTimeOffset? BuscaIniciadaEmUtc,
    string? ResponsavelBusca,
    byte[] VersaoPedido,
    string Conferencia,
    long DuracaoTicks,
    int Diarias,
    int TarifaVersao,
    decimal TarifaValorSnapshot,
    decimal TotalOriginal,
    decimal Descontos,
    decimal Pagamentos,
    decimal Estornos,
    decimal Cobrado,
    decimal PagoLiquido,
    decimal Saldo,
    int RevisaoRazao,
    IReadOnlyList<LancamentoResumo> Lancamentos,
    AutorizacaoResumo AutorizacaoDocumental,
    AutorizacaoResumo AutorizacaoFinanceira,
    bool VistoriaSaidaRegistrada = false,
    ResumoEntrega? Entrega = null);

/// <summary>Comandos e consultas do pedido, da fila e do início de busca.</summary>
public interface IServicoRetiradas
{
    Task<ResultadoPedidoRetirada> RegistrarPedidoAsync(
        AtorAtual ator,
        Guid atendimentoId,
        RequisicaoRegistrarPedidoRetirada requisicao,
        Guid chaveIdempotente,
        string? correlacao,
        CancellationToken cancellationToken = default);

    Task<object> IniciarBuscaAsync(
        AtorAtual ator,
        Guid pedidoId,
        byte[] versaoEsperada,
        string? correlacao,
        CancellationToken cancellationToken = default);
}

/// <summary>Fila e detalhe paginados e projetados por perfil.</summary>
public interface IServicoConsultaRetiradas
{
    Task<object> ListarAsync(
        AtorAtual ator,
        string? estado,
        int pagina,
        int tamanho,
        CancellationToken cancellationToken = default);

    Task<object> ObterAsync(
        AtorAtual ator,
        Guid pedidoId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumo opcional da retirada para o detalhe do atendimento. Devolve nulo
    /// quando o atendimento ainda não possui pedido.
    /// </summary>
    Task<object?> ObterResumoPorAtendimentoAsync(
        AtorAtual ator,
        Guid atendimentoId,
        CancellationToken cancellationToken = default);
}
