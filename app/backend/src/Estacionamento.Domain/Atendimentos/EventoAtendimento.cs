namespace Estacionamento.Domain.Atendimentos;

/// <summary>
/// Evento append-only do histórico de um atendimento. Contém apenas tipo, ator
/// opaco, horário UTC e correlação; nunca documento, vistoria ou valores.
/// </summary>
public sealed class EventoAtendimento
{
    public long Id { get; set; }
    public Guid AtendimentoId { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public string AtorOpaco { get; set; } = string.Empty;
    public DateTimeOffset OcorridoEmUtc { get; set; }
    public string? Correlacao { get; set; }
    public string? DetalheOpaco { get; set; }
}
