using Estacionamento.Application.Auditoria;
using Estacionamento.Application.Comum;
using Estacionamento.Application.Tempo;
using Estacionamento.Application.Vagas;
using Estacionamento.Domain.Atendimentos;
using Estacionamento.Domain.Vagas;
using Estacionamento.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Estacionamento.Infrastructure.Servicos;

/// <summary>
/// Cadastro administrativo e consulta paginada de vagas. A situação é derivada
/// por EXISTS sobre a ocupação aberta; nenhuma reserva é criada pela consulta.
/// </summary>
public sealed class ServicoVagas : IServicoVagas
{
    private readonly EstacionamentoDbContext _db;
    private readonly IAuditoriaNegocio _auditoria;
    private readonly IRelogio _relogio;

    public ServicoVagas(
        EstacionamentoDbContext db,
        IAuditoriaNegocio auditoria,
        IRelogio relogio)
    {
        _db = db;
        _auditoria = auditoria;
        _relogio = relogio;
    }

    public async Task<VagaResumo> CadastrarAsync(
        AtorAtual ator,
        RequisicaoCadastrarVaga requisicao,
        string? correlacao,
        CancellationToken cancellationToken = default)
    {
        if (!ator.EhAdministrador)
        {
            throw ExcecaoAplicacao.AcessoNegado();
        }

        var normalizado = CodigoVaga.Normalizar(requisicao.Codigo);
        if (!CodigoVaga.EhValido(normalizado))
        {
            throw ExcecaoAplicacao.Validacao(
                "codigo_vaga_invalido",
                $"O código da vaga deve ter de 1 a {CodigoVaga.TamanhoMaximo} caracteres.");
        }

        var agora = _relogio.UtcNow;
        var vaga = new Vaga
        {
            Id = Guid.NewGuid(),
            Codigo = normalizado,
            CodigoNormalizado = normalizado,
            CriadoPorId = ator.Id,
            CriadoEmUtc = agora
        };

        await using var transacao = await _db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            _db.Vagas.Add(vaga);
            _auditoria.Adicionar(new EventoAuditoriaNegocio(
                TiposAuditoriaNegocio.VagaCadastrada,
                ator.Id,
                "vaga",
                vaga.Id,
                agora,
                correlacao,
                null));

            await _db.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException excecao) when (ErrosSql.EhViolacaoUnicidade(excecao))
        {
            await transacao.RollbackAsync(cancellationToken);
            throw ExcecaoAplicacao.Conflito(
                "space_code_conflict",
                "Já existe uma vaga com o código informado.");
        }

        return Mapear(vaga, SituacaoVaga.Livre);
    }

    public async Task<Pagina<VagaResumo>> ConsultarAsync(
        AtorAtual ator,
        FiltroVagas filtro,
        int pagina,
        int tamanho,
        CancellationToken cancellationToken = default)
    {
        var situacao = NormalizarSituacao(filtro.Situacao);
        var codigo = string.IsNullOrWhiteSpace(filtro.Codigo)
            ? null
            : CodigoVaga.Normalizar(filtro.Codigo);

        var consulta = _db.Vagas.AsNoTracking();

        if (codigo is not null)
        {
            consulta = consulta.Where(vaga => vaga.CodigoNormalizado.Contains(codigo));
        }

        if (situacao == SituacaoVaga.Livre)
        {
            consulta = consulta.Where(vaga => !_db.Ocupacoes.Any(ocupacao =>
                ocupacao.VagaId == vaga.Id && ocupacao.FimUtc == null));
        }
        else if (situacao == SituacaoVaga.Ocupada)
        {
            consulta = consulta.Where(vaga => _db.Ocupacoes.Any(ocupacao =>
                ocupacao.VagaId == vaga.Id && ocupacao.FimUtc == null));
        }

        var total = await consulta.CountAsync(cancellationToken);
        var itens = await consulta
            .OrderBy(vaga => vaga.CodigoNormalizado)
            .Skip((pagina - 1) * tamanho)
            .Take(tamanho)
            .Select(vaga => new
            {
                vaga.Id,
                vaga.Codigo,
                vaga.RowVersion,
                Ocupada = _db.Ocupacoes.Any(ocupacao =>
                    ocupacao.VagaId == vaga.Id && ocupacao.FimUtc == null)
            })
            .ToListAsync(cancellationToken);

        return new Pagina<VagaResumo>(
            itens.Select(item => new VagaResumo(
                item.Id,
                item.Codigo,
                item.Ocupada ? SituacaoVaga.Ocupada : SituacaoVaga.Livre,
                Convert.ToBase64String(item.RowVersion)))
                .ToArray(),
            pagina,
            tamanho,
            total);
    }

    private static string? NormalizarSituacao(string? situacao)
    {
        if (string.IsNullOrWhiteSpace(situacao))
        {
            return null;
        }

        return situacao.Trim().ToLowerInvariant() switch
        {
            SituacaoVaga.Livre => SituacaoVaga.Livre,
            SituacaoVaga.Ocupada => SituacaoVaga.Ocupada,
            _ => throw ExcecaoAplicacao.Validacao(
                "situacao_vaga_invalida",
                "A situação deve ser 'livre' ou 'ocupada'.")
        };
    }

    private static VagaResumo Mapear(Vaga vaga, string situacao) =>
        new(vaga.Id, vaga.Codigo, situacao, Convert.ToBase64String(vaga.RowVersion));
}
