namespace Estacionamento.Domain.Auditoria;

/// <summary>
/// Auditoria de negócio append-only, separada dos logs técnicos. Guarda ator,
/// alvo, tipo e horário UTC, sem conteúdo sensível.
/// </summary>
public sealed class EventoAuditoriaNegocio
{
    public long Id { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public Guid AtorId { get; set; }
    public string AlvoTipo { get; set; } = string.Empty;
    public Guid AlvoId { get; set; }
    public DateTimeOffset OcorridoEmUtc { get; set; }
    public string? Correlacao { get; set; }
    public string? Detalhe { get; set; }
}
