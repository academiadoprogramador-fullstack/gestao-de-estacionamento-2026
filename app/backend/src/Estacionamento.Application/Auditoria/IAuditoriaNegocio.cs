namespace Estacionamento.Application.Auditoria;

/// <summary>
/// Evento de auditoria de negócio append-only. O ator e o alvo são
/// identificadores opacos; nenhum texto sensível entra na assinatura.
/// </summary>
public sealed record EventoAuditoriaNegocio(
    string Tipo,
    Guid AtorId,
    string AlvoTipo,
    Guid AlvoId,
    DateTimeOffset OcorridoEmUtc,
    string? Correlacao,
    string? Detalhe);

/// <summary>
/// Porta de auditoria de negócio. A gravação pode participar da transação do
/// caso de uso ou ser confirmada antes de uma leitura sensível.
/// </summary>
public interface IAuditoriaNegocio
{
    void Adicionar(EventoAuditoriaNegocio evento);

    Task RegistrarAsync(EventoAuditoriaNegocio evento, CancellationToken cancellationToken = default);
}
