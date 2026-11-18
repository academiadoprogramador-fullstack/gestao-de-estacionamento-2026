using System.Data;
using Estacionamento.Application.Auditoria;
using Estacionamento.Application.Comum;
using Estacionamento.Application.Tempo;
using Estacionamento.Application.Tarifas;
using Estacionamento.Domain.Atendimentos;
using Estacionamento.Domain.Tarifas;
using Estacionamento.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Estacionamento.Infrastructure.Servicos;

/// <summary>
/// Publicação imutável e consulta de tarifas. A publicação bloqueia a
/// configuração singleton com <c>UPDLOCK</c> e compara a versão-base corrente;
/// duas publicações da mesma base resultam em conflito explícito.
/// </summary>
public sealed class ServicoTarifas : IServicoTarifas
{
    private readonly EstacionamentoDbContext _db;
    private readonly IAuditoriaNegocio _auditoria;
    private readonly IRelogio _relogio;

    public ServicoTarifas(
        EstacionamentoDbContext db,
        IAuditoriaNegocio auditoria,
        IRelogio relogio)
    {
        _db = db;
        _auditoria = auditoria;
        _relogio = relogio;
    }

    public async Task<TarifaResumo> PublicarAsync(
        AtorAtual ator,
        RequisicaoPublicarTarifa requisicao,
        string? correlacao,
        CancellationToken cancellationToken = default)
    {
        if (!ator.EhAdministrador)
        {
            throw ExcecaoAplicacao.AcessoNegado();
        }

        if (!ValorDiaria.TentarCriar(requisicao.Valor, out var valor))
        {
            throw ExcecaoAplicacao.Validacao(
                "valor_tarifa_invalido",
                "O valor da diária deve ser positivo, com no máximo duas casas decimais e dentro do limite representável.");
        }

        await using var transacao = await _db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        try
        {
            var configuracao = await _db.ConfiguracaoTarifa
                .FromSqlInterpolated(
                    $"SELECT * FROM [ConfiguracaoTarifa] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {ConfiguracaoTarifa.IdSingleton}")
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw ExcecaoAplicacao.RegraNaoSatisfeita(
                    "tarifa_indisponivel",
                    "A configuração de tarifa não está disponível.");

            var versaoCorrente = configuracao.VersaoCorrenteId is null
                ? (int?)null
                : await _db.TarifasDiarias
                    .AsNoTracking()
                    .Where(tarifa => tarifa.Id == configuracao.VersaoCorrenteId)
                    .Select(tarifa => (int?)tarifa.Versao)
                    .SingleAsync(cancellationToken);

            if (requisicao.VersaoBaseEsperada != versaoCorrente)
            {
                throw ExcecaoAplicacao.Conflito(
                    "stale_rate_version",
                    "A configuração corrente de tarifa foi alterada. Recarregue a versão antes de publicar.");
            }

            var proximaVersao = (await _db.TarifasDiarias.MaxAsync(
                tarifa => (int?)tarifa.Versao,
                cancellationToken) ?? 0) + 1;

            var agora = _relogio.UtcNow;
            var nova = new TarifaDiaria
            {
                Id = Guid.NewGuid(),
                Versao = proximaVersao,
                Valor = valor.Valor,
                PublicadoPorId = ator.Id,
                PublicadoEmUtc = agora
            };

            configuracao.VersaoCorrenteId = nova.Id;
            _db.TarifasDiarias.Add(nova);
            _auditoria.Adicionar(new EventoAuditoriaNegocio(
                TiposAuditoriaNegocio.TarifaPublicada,
                ator.Id,
                "tarifa",
                nova.Id,
                agora,
                correlacao,
                $"versao:{proximaVersao}"));

            await _db.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);

            return new TarifaResumo(
                nova.Versao,
                nova.Valor,
                ator.Id,
                await ObterNomeAsync(ator.Id, cancellationToken),
                nova.PublicadoEmUtc);
        }
        catch (DbUpdateException excecao) when (ErrosSql.EhViolacaoUnicidade(excecao))
        {
            await transacao.RollbackAsync(cancellationToken);
            throw ExcecaoAplicacao.Conflito(
                "stale_rate_version",
                "Outra publicação foi confirmada. Recarregue a versão corrente.");
        }
    }

    public async Task<TarifaCorrente> ObterCorrenteAsync(CancellationToken cancellationToken = default)
    {
        var configuracao = await _db.ConfiguracaoTarifa
            .AsNoTracking()
            .SingleOrDefaultAsync(
                registro => registro.Id == ConfiguracaoTarifa.IdSingleton,
                cancellationToken);

        if (configuracao?.VersaoCorrenteId is null)
        {
            return new TarifaCorrente(false, null, null, null, null, null);
        }

        var tarifa = await _db.TarifasDiarias
            .AsNoTracking()
            .SingleAsync(registro => registro.Id == configuracao.VersaoCorrenteId, cancellationToken);

        return new TarifaCorrente(
            true,
            tarifa.Versao,
            tarifa.Valor,
            tarifa.PublicadoPorId,
            tarifa.PublicadoEmUtc,
            tarifa.Versao);
    }

    public async Task<Pagina<TarifaResumo>> ListarAsync(
        AtorAtual ator,
        int pagina,
        int tamanho,
        CancellationToken cancellationToken = default)
    {
        if (!ator.EhAdministrador)
        {
            throw ExcecaoAplicacao.AcessoNegado();
        }

        var consulta = _db.TarifasDiarias.AsNoTracking();

        var total = await consulta.CountAsync(cancellationToken);
        var itens = await consulta
            .OrderByDescending(tarifa => tarifa.Versao)
            .Skip((pagina - 1) * tamanho)
            .Take(tamanho)
            .Join(
                _db.Users.AsNoTracking(),
                tarifa => tarifa.PublicadoPorId,
                usuario => usuario.Id,
                (tarifa, usuario) => new TarifaResumo(
                    tarifa.Versao,
                    tarifa.Valor,
                    tarifa.PublicadoPorId,
                    usuario.Nome,
                    tarifa.PublicadoEmUtc))
            .ToListAsync(cancellationToken);

        return new Pagina<TarifaResumo>(itens, pagina, tamanho, total);
    }

    private async Task<string> ObterNomeAsync(Guid usuarioId, CancellationToken cancellationToken) =>
        await _db.Users
            .AsNoTracking()
            .Where(usuario => usuario.Id == usuarioId)
            .Select(usuario => usuario.Nome)
            .SingleAsync(cancellationToken);
}
