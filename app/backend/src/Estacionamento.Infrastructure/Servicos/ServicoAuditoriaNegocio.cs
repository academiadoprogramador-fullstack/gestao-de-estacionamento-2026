using Estacionamento.Application.Auditoria;
using Estacionamento.Infrastructure.Persistencia;
using EventoAuditoriaNegocioEntidade = Estacionamento.Domain.Auditoria.EventoAuditoriaNegocio;

namespace Estacionamento.Infrastructure.Servicos;

/// <summary>
/// Auditoria de negócio append-only no mesmo contexto relacional. Nenhum
/// conteúdo sensível é aceito na assinatura do evento.
/// </summary>
public sealed class ServicoAuditoriaNegocio : IAuditoriaNegocio
{
    private readonly EstacionamentoDbContext _db;

    public ServicoAuditoriaNegocio(EstacionamentoDbContext db)
    {
        _db = db;
    }

    public void Adicionar(EventoAuditoriaNegocio evento)
    {
        _db.EventosAuditoriaNegocio.Add(new EventoAuditoriaNegocioEntidade
        {
            Tipo = evento.Tipo,
            AtorId = evento.AtorId,
            AlvoTipo = evento.AlvoTipo,
            AlvoId = evento.AlvoId,
            OcorridoEmUtc = evento.OcorridoEmUtc,
            Correlacao = evento.Correlacao,
            Detalhe = evento.Detalhe
        });
    }

    public async Task RegistrarAsync(
        EventoAuditoriaNegocio evento,
        CancellationToken cancellationToken = default)
    {
        Adicionar(evento);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
