using Estacionamento.Application.Auditoria;
using Estacionamento.Infrastructure.Persistencia;
using Estacionamento.Infrastructure.Persistencia.Entidades;

namespace Estacionamento.Infrastructure.Servicos;

/// <summary>
/// Grava eventos append-only no contexto atual. Nenhum valor sensível é
/// aceito na assinatura do evento.
/// </summary>
public sealed class ServicoAuditoria : IServicoAuditoria
{
    private readonly EstacionamentoDbContext _db;

    public ServicoAuditoria(EstacionamentoDbContext db)
    {
        _db = db;
    }

    public void Adicionar(EventoAuditoriaAutenticacao evento)
    {
        _db.EventosAuditoria.Add(new EventoAuditoria
        {
            Tipo = evento.Tipo,
            AtorOpaco = evento.AtorOpaco,
            AlvoOpaco = evento.AlvoOpaco,
            Correlacao = evento.Correlacao,
            Detalhe = evento.Detalhe,
            OcorridoEmUtc = evento.OcorridoEmUtc
        });
    }

    public async Task RegistrarAsync(EventoAuditoriaAutenticacao evento, CancellationToken cancellationToken = default)
    {
        Adicionar(evento);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
