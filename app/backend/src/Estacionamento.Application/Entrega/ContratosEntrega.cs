using Estacionamento.Application.Comum;
using Estacionamento.Application.Fotos;
using Estacionamento.Domain.Entrega;

namespace Estacionamento.Application.Entrega;

/// <summary>
/// Registro da vistoria de saída. Não existe propriedade para foto, documento,
/// ator, horário ou pedido: esses vínculos são sempre do servidor e
/// propriedades desconhecidas são rejeitadas pelo serializador estrito.
/// </summary>
public sealed record RequisicaoVistoriaSaida(
    string? Texto,
    byte[]? VersaoAtendimentoEsperada);

/// <summary>Recibo mínimo da vistoria, sem o texto e sem dados financeiros.</summary>
public sealed record ResultadoVistoriaSaida(
    Guid VistoriaId,
    Guid AtendimentoId,
    Guid PedidoId,
    string Ator,
    DateTimeOffset RegistradaEmUtc,
    string Location);

/// <summary>Leitura explícita da vistoria, devolvida somente após auditoria.</summary>
public sealed record LeituraVistoriaSaida(
    string Conteudo,
    string Ator,
    DateTimeOffset RegistradaEmUtc);

/// <summary>
/// Confirmação explícita da entrega com todas as versões-base. A confirmação
/// precisa ser <c>true</c>; o servidor não reserva a entrega na prontidão.
/// </summary>
public sealed record RequisicaoConfirmarEntrega(
    bool? Confirmacao,
    byte[]? VersaoAtendimento,
    byte[]? VersaoPedido,
    byte[]? VersaoRazao,
    string? VersaoControleDocumental,
    string? VersaoControleFinanceira);

/// <summary>
/// Vínculo histórico da entrega com a vaga liberada naquela visita. Não
/// representa a disponibilidade corrente da vaga.
/// </summary>
public sealed record ResumoEntrega(
    Guid EntregaId,
    string Responsavel,
    DateTimeOffset EntregueEmUtc,
    Guid VagaId,
    string VagaCodigo,
    DateTimeOffset LiberadaEmUtc);

/// <summary>Versões-base exigidas para confirmar a entrega.</summary>
public sealed record VersoesEntrega(
    byte[] VersaoAtendimento,
    byte[] VersaoPedido,
    byte[] VersaoRazao,
    string VersaoControleDocumental,
    string VersaoControleFinanceira);

/// <summary>
/// Prontidão informativa da entrega. Não reserva o pedido e não inclui texto,
/// justificativa ou documento.
/// </summary>
public sealed record ProntidaoEntrega(
    Guid PedidoId,
    Guid AtendimentoId,
    string Identificador,
    string Estado,
    string VagaCodigo,
    bool VistoriaRegistrada,
    string Conferencia,
    decimal Saldo,
    IReadOnlyList<CondicaoEntrega> Condicoes,
    IReadOnlyList<string> Bloqueios,
    bool Pronta,
    ResumoEntrega? Entrega,
    VersoesEntrega Versoes,
    ResumoFotosVistoria? Fotos = null);

/// <summary>Recibo mínimo da entrega confirmada ou repetida.</summary>
public sealed record ResultadoEntrega(
    Guid EntregaId,
    Guid PedidoId,
    Guid AtendimentoId,
    string Estado,
    DateTimeOffset EntregueEmUtc,
    string Ator,
    ResumoEntrega Liberacao,
    string Location);

/// <summary>Registro e leitura explícita da vistoria de saída.</summary>
public interface IServicoVistoriaSaida
{
    Task<ResultadoVistoriaSaida> RegistrarAsync(
        AtorAtual ator,
        Guid atendimentoId,
        RequisicaoVistoriaSaida requisicao,
        Guid chaveIdempotente,
        string? correlacao,
        CancellationToken cancellationToken = default);

    Task<LeituraVistoriaSaida> LerAsync(
        AtorAtual ator,
        Guid atendimentoId,
        string? correlacao,
        CancellationToken cancellationToken = default);
}

/// <summary>Prontidão e confirmação transacional da entrega.</summary>
public interface IServicoEntrega
{
    Task<ProntidaoEntrega> ObterProntidaoAsync(
        AtorAtual ator,
        Guid pedidoId,
        CancellationToken cancellationToken = default);

    Task<ResultadoEntrega> ConfirmarAsync(
        AtorAtual ator,
        Guid pedidoId,
        RequisicaoConfirmarEntrega requisicao,
        Guid chaveIdempotente,
        string? correlacao,
        CancellationToken cancellationToken = default);
}
