using Estacionamento.Application.Comum;

namespace Estacionamento.Application.Atendimentos;

/// <summary>
/// Entrada completa. Não admite tarifa, horário, operador ou identificador do
/// cliente; esses valores são sempre do servidor.
/// </summary>
public sealed record RequisicaoRegistrarEntrada(
    string Placa,
    string Modelo,
    string Titular,
    string Documento,
    string Vistoria,
    Guid VagaId);

/// <summary>
/// Resultado mínimo da entrada, usado também no replay idempotente. Não contém
/// documento, vistoria ou tarifa.
/// </summary>
public sealed record ResultadoEntrada(
    Guid AtendimentoId,
    string Identificador,
    Guid VagaId,
    string VagaCodigo,
    string Estado,
    DateTimeOffset EntradaUtc,
    string Location);

/// <summary>Comando transacional de check-in.</summary>
public interface IServicoAtendimentos
{
    Task<ResultadoEntrada> RegistrarEntradaAsync(
        AtorAtual ator,
        RequisicaoRegistrarEntrada requisicao,
        Guid chaveIdempotente,
        string? correlacao,
        CancellationToken cancellationToken = default);
}

/// <summary>Item de busca, sem documento, vistoria ou dados financeiros.</summary>
public sealed record AtendimentoLista(
    Guid Id,
    string Identificador,
    string Placa,
    string Modelo,
    string Titular,
    string Estado,
    string VagaCodigo,
    DateTimeOffset EntradaUtc);

/// <summary>Detalhe operacional entregue a todos os perfis.</summary>
public sealed record AtendimentoDetalheOperacional(
    Guid Id,
    string Identificador,
    string Placa,
    string Modelo,
    string Titular,
    string Estado,
    string VagaCodigo,
    DateTimeOffset EntradaUtc,
    byte[] VersaoAtendimento);

/// <summary>
/// Detalhe administrativo/operador. O DTO do Manobrista é outro tipo e não
/// possui estas propriedades financeiras.
/// </summary>
public sealed record AtendimentoDetalheAdministrativo(
    Guid Id,
    string Identificador,
    string Placa,
    string Modelo,
    string Titular,
    string Estado,
    string VagaCodigo,
    DateTimeOffset EntradaUtc,
    byte[] VersaoAtendimento,
    int TarifaVersao,
    decimal TarifaValorSnapshot);

/// <summary>Evento do histórico, sem conteúdo sensível.</summary>
public sealed record EventoHistorico(
    string Tipo,
    string AtorOpaco,
    DateTimeOffset OcorridoEmUtc);

/// <summary>Conteúdo sensível devolvido somente após auditoria confirmada.</summary>
public sealed record LeituraSensivel(string Conteudo);

/// <summary>Busca, detalhe, histórico e leituras sensíveis.</summary>
public interface IServicoConsultaAtendimentos
{
    Task<Pagina<AtendimentoLista>> BuscarAsync(
        AtorAtual ator,
        string? placa,
        string? identificador,
        int pagina,
        int tamanho,
        CancellationToken cancellationToken = default);

    Task<object> ObterDetalheAsync(
        AtorAtual ator,
        Guid atendimentoId,
        CancellationToken cancellationToken = default);

    Task<Pagina<EventoHistorico>> ListarHistoricoAsync(
        AtorAtual ator,
        Guid atendimentoId,
        int pagina,
        int tamanho,
        CancellationToken cancellationToken = default);

    Task<LeituraSensivel> LerDocumentoAsync(
        AtorAtual ator,
        Guid atendimentoId,
        string? correlacao,
        CancellationToken cancellationToken = default);

    Task<LeituraSensivel> LerVistoriaEntradaAsync(
        AtorAtual ator,
        Guid atendimentoId,
        string? correlacao,
        CancellationToken cancellationToken = default);
}
