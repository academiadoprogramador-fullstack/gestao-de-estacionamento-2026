namespace Estacionamento.Infrastructure.Persistencia.Entidades;

/// <summary>
/// Evento de auditoria de autenticação append-only. Não contém senha, token,
/// refresh, e-mail ou outro dado pessoal; o ator é uma referência opaca.
/// </summary>
public sealed class EventoAuditoria
{
    public long Id { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public string? AtorOpaco { get; set; }
    public string? AlvoOpaco { get; set; }
    public string? Correlacao { get; set; }
    public string? Detalhe { get; set; }
    public DateTimeOffset OcorridoEmUtc { get; set; }
}
