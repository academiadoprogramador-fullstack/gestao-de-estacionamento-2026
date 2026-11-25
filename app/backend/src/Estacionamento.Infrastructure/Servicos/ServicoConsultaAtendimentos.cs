using Estacionamento.Application.Atendimentos;
using Estacionamento.Application.Auditoria;
using Estacionamento.Application.Comum;
using Estacionamento.Application.Entrega;
using Estacionamento.Application.Retiradas;
using Estacionamento.Application.Seguranca;
using Estacionamento.Application.Tempo;
using Estacionamento.Domain.Atendimentos;
using Estacionamento.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Estacionamento.Infrastructure.Servicos;

/// <summary>
/// Busca exata, detalhe e histórico projetados por perfil, além de leitura
/// explícita de documento e vistoria com auditoria confirmada antes de
/// decifrar. Listagens nunca incluem conteúdo sensível.
/// </summary>
public sealed class ServicoConsultaAtendimentos : IServicoConsultaAtendimentos
{
    private readonly EstacionamentoDbContext _db;
    private readonly ICriptografiaCampos _criptografia;
    private readonly IAuditoriaNegocio _auditoria;
    private readonly IRelogio _relogio;
    private readonly IServicoConsultaRetiradas _retiradas;

    public ServicoConsultaAtendimentos(
        EstacionamentoDbContext db,
        ICriptografiaCampos criptografia,
        IAuditoriaNegocio auditoria,
        IRelogio relogio,
        IServicoConsultaRetiradas retiradas)
    {
        _db = db;
        _criptografia = criptografia;
        _auditoria = auditoria;
        _relogio = relogio;
        _retiradas = retiradas;
    }

    public async Task<Pagina<AtendimentoLista>> BuscarAsync(
        AtorAtual ator,
        string? placa,
        string? identificador,
        int pagina,
        int tamanho,
        CancellationToken cancellationToken = default)
    {
        var temPlaca = !string.IsNullOrWhiteSpace(placa);
        var temIdentificador = !string.IsNullOrWhiteSpace(identificador);

        if (temPlaca == temIdentificador)
        {
            throw ExcecaoAplicacao.Validacao(
                "filtro_invalido",
                "Informe exatamente um filtro: placa ou identificador.");
        }

        IQueryable<Atendimento> consulta = _db.Atendimentos.AsNoTracking();

        if (temPlaca)
        {
            var placaNormalizada = NormalizadorPlaca.Normalizar(placa);
            if (!NormalizadorPlaca.EhValido(placaNormalizada))
            {
                throw ExcecaoAplicacao.Validacao(
                    "placa_invalida",
                    "Informe uma placa com até 16 caracteres alfanuméricos.");
            }

            consulta = consulta.Where(atendimento => atendimento.Placa == placaNormalizada);
        }
        else
        {
            var identificadorNormalizado = IdentificadorAtendimento.Normalizar(identificador);
            if (!IdentificadorAtendimento.EhValido(identificadorNormalizado))
            {
                throw ExcecaoAplicacao.Validacao(
                    "identificador_invalido",
                    "Informe um identificador no formato AT-AAAA-000000.");
            }

            consulta = consulta.Where(atendimento =>
                atendimento.IdentificadorPublico == identificadorNormalizado);
        }

        var total = await consulta.CountAsync(cancellationToken);

        var itens = await (
                from atendimento in consulta
                join ocupacao in _db.Ocupacoes.AsNoTracking()
                    on atendimento.Id equals ocupacao.AtendimentoId into ocupacoes
                from ocupacao in ocupacoes.DefaultIfEmpty()
                join vaga in _db.Vagas.AsNoTracking()
                    on ocupacao.VagaId equals vaga.Id into vagas
                from vaga in vagas.DefaultIfEmpty()
                orderby atendimento.EntradaUtc descending, atendimento.IdentificadorPublico descending
                select new AtendimentoLista(
                    atendimento.Id,
                    atendimento.IdentificadorPublico ?? string.Empty,
                    atendimento.Placa,
                    atendimento.Modelo,
                    atendimento.Titular,
                    atendimento.Estado,
                    vaga == null ? string.Empty : vaga.Codigo,
                    atendimento.EntradaUtc))
            .Skip((pagina - 1) * tamanho)
            .Take(tamanho)
            .ToListAsync(cancellationToken);

        return new Pagina<AtendimentoLista>(itens, pagina, tamanho, total);
    }

    public async Task<Pagina<AtendimentoLista>> ListarAbertosAsync(
        AtorAtual ator,
        int pagina,
        int tamanho,
        CancellationToken cancellationToken = default)
    {
        var consulta =
            from atendimento in _db.Atendimentos.AsNoTracking()
            join ocupacao in _db.Ocupacoes.AsNoTracking()
                on atendimento.Id equals ocupacao.AtendimentoId
            join vaga in _db.Vagas.AsNoTracking()
                on ocupacao.VagaId equals vaga.Id
            join pedido in _db.PedidosRetirada.AsNoTracking()
                on atendimento.Id equals pedido.AtendimentoId into pedidos
            from pedido in pedidos.DefaultIfEmpty()
            where atendimento.Estado != EstadoAtendimento.Entregue &&
                  ocupacao.FimUtc == null
            orderby atendimento.EntradaUtc descending, atendimento.IdentificadorPublico descending
            select new
            {
                Atendimento = atendimento,
                VagaCodigo = vaga.Codigo,
                PedidoId = pedido == null ? (Guid?)null : pedido.Id,
                EstadoRetirada = pedido == null ? null : pedido.Estado
            };

        var total = await consulta.CountAsync(cancellationToken);
        var itens = await consulta
            .Skip((pagina - 1) * tamanho)
            .Take(tamanho)
            .Select(item => new AtendimentoLista(
                item.Atendimento.Id,
                item.Atendimento.IdentificadorPublico ?? string.Empty,
                item.Atendimento.Placa,
                item.Atendimento.Modelo,
                item.Atendimento.Titular,
                item.Atendimento.Estado,
                item.VagaCodigo,
                item.Atendimento.EntradaUtc,
                item.PedidoId,
                item.EstadoRetirada))
            .ToListAsync(cancellationToken);

        return new Pagina<AtendimentoLista>(itens, pagina, tamanho, total);
    }

    public async Task<object> ObterDetalheAsync(
        AtorAtual ator,
        Guid atendimentoId,
        CancellationToken cancellationToken = default)
    {
        var dados = await (
                from atendimento in _db.Atendimentos.AsNoTracking()
                join ocupacao in _db.Ocupacoes.AsNoTracking()
                    on atendimento.Id equals ocupacao.AtendimentoId
                join vaga in _db.Vagas.AsNoTracking()
                    on ocupacao.VagaId equals vaga.Id
                where atendimento.Id == atendimentoId
                orderby ocupacao.FimUtc == null descending, ocupacao.InicioUtc descending
                select new
                {
                    Atendimento = atendimento,
                    VagaCodigo = vaga.Codigo
                })
            .FirstOrDefaultAsync(cancellationToken);

        if (dados is null)
        {
            throw ExcecaoAplicacao.NaoEncontrado(
                "atendimento_nao_encontrado",
                "Atendimento não encontrado.");
        }

        var atendimentoAtual = dados.Atendimento;
        var retirada = await _retiradas.ObterResumoPorAtendimentoAsync(
            ator,
            atendimentoAtual.Id,
            cancellationToken);
        var vistoriaSaidaRegistrada = await _db.VistoriasSaida
            .AsNoTracking()
            .AnyAsync(vistoria => vistoria.AtendimentoId == atendimentoAtual.Id, cancellationToken);
        var entrega = await CarregarEntregaAsync(atendimentoAtual.Id, cancellationToken);

        if (!ator.EhOperador)
        {
            // O DTO do Manobrista não possui propriedades financeiras.
            return new AtendimentoDetalheOperacional(
                atendimentoAtual.Id,
                atendimentoAtual.IdentificadorPublico ?? string.Empty,
                atendimentoAtual.Placa,
                atendimentoAtual.Modelo,
                atendimentoAtual.Titular,
                atendimentoAtual.Estado,
                dados.VagaCodigo,
                atendimentoAtual.EntradaUtc,
                atendimentoAtual.RowVersion,
                retirada as ResumoRetiradaOperacional,
                vistoriaSaidaRegistrada,
                entrega);
        }

        var versaoTarifa = await _db.TarifasDiarias
            .AsNoTracking()
            .Where(tarifa => tarifa.Id == atendimentoAtual.TarifaVersaoId)
            .Select(tarifa => tarifa.Versao)
            .SingleAsync(cancellationToken);

        return new AtendimentoDetalheAdministrativo(
            atendimentoAtual.Id,
            atendimentoAtual.IdentificadorPublico ?? string.Empty,
            atendimentoAtual.Placa,
            atendimentoAtual.Modelo,
            atendimentoAtual.Titular,
            atendimentoAtual.Estado,
            dados.VagaCodigo,
            atendimentoAtual.EntradaUtc,
            atendimentoAtual.RowVersion,
            versaoTarifa,
            atendimentoAtual.TarifaValorSnapshot,
            retirada as ResumoRetiradaFinanceiro,
            vistoriaSaidaRegistrada,
            entrega);
    }

    private async Task<ResumoEntrega?> CarregarEntregaAsync(
        Guid atendimentoId,
        CancellationToken cancellationToken)
    {
        var dados = await (
                from entrega in _db.EntregasVeiculo.AsNoTracking()
                join usuario in _db.Users.AsNoTracking()
                    on entrega.AtorId equals usuario.Id
                join vaga in _db.Vagas.AsNoTracking()
                    on entrega.VagaId equals vaga.Id
                join ocupacao in _db.Ocupacoes.AsNoTracking()
                    on entrega.OcupacaoId equals ocupacao.Id
                where entrega.AtendimentoId == atendimentoId
                select new
                {
                    entrega.Id,
                    usuario.Nome,
                    entrega.EntregueEmUtc,
                    entrega.VagaId,
                    vaga.Codigo,
                    ocupacao.FimUtc
                })
            .SingleOrDefaultAsync(cancellationToken);

        return dados is null
            ? null
            : new ResumoEntrega(
                dados.Id,
                dados.Nome,
                dados.EntregueEmUtc,
                dados.VagaId,
                dados.Codigo,
                dados.FimUtc ?? dados.EntregueEmUtc);
    }

    public async Task<Pagina<EventoHistorico>> ListarHistoricoAsync(
        AtorAtual ator,
        Guid atendimentoId,
        int pagina,
        int tamanho,
        CancellationToken cancellationToken = default)
    {
        var existe = await _db.Atendimentos
            .AsNoTracking()
            .AnyAsync(atendimento => atendimento.Id == atendimentoId, cancellationToken);

        if (!existe)
        {
            throw ExcecaoAplicacao.NaoEncontrado(
                "atendimento_nao_encontrado",
                "Atendimento não encontrado.");
        }

        var consulta = _db.EventosAtendimento
            .AsNoTracking()
            .Where(evento => evento.AtendimentoId == atendimentoId);

        if (!ator.EhOperador)
        {
            var permitidos = TiposEventoAtendimento.VisiveisAoManobrista;
            consulta = consulta.Where(evento => permitidos.Contains(evento.Tipo));
        }

        var total = await consulta.CountAsync(cancellationToken);
        var itens = await consulta
            .OrderBy(evento => evento.OcorridoEmUtc)
            .ThenBy(evento => evento.Id)
            .Skip((pagina - 1) * tamanho)
            .Take(tamanho)
            .Select(evento => new EventoHistorico(
                evento.Tipo,
                evento.AtorOpaco,
                evento.OcorridoEmUtc))
            .ToListAsync(cancellationToken);

        return new Pagina<EventoHistorico>(itens, pagina, tamanho, total);
    }

    public async Task<LeituraSensivel> LerDocumentoAsync(
        AtorAtual ator,
        Guid atendimentoId,
        string? correlacao,
        CancellationToken cancellationToken = default)
    {
        if (!ator.EhOperador)
        {
            throw ExcecaoAplicacao.AcessoNegado();
        }

        var atendimento = await _db.Atendimentos
            .AsNoTracking()
            .SingleOrDefaultAsync(registro => registro.Id == atendimentoId, cancellationToken)
            ?? throw ExcecaoAplicacao.NaoEncontrado(
                "atendimento_nao_encontrado",
                "Atendimento não encontrado.");

        await AuditarLeituraAsync(
            ator,
            atendimentoId,
            TiposEventoAtendimento.DocumentoLido,
            TiposAuditoriaNegocio.DocumentoLido,
            correlacao,
            cancellationToken);

        return new LeituraSensivel(
            _criptografia.Decifrar(
                PropositosCriptografia.Documento,
                atendimentoId,
                atendimento.DocumentoTitularCifrado));
    }

    public async Task<LeituraSensivel> LerVistoriaEntradaAsync(
        AtorAtual ator,
        Guid atendimentoId,
        string? correlacao,
        CancellationToken cancellationToken = default)
    {
        var vistoria = await _db.VistoriasEntrada
            .AsNoTracking()
            .SingleOrDefaultAsync(
                registro => registro.AtendimentoId == atendimentoId,
                cancellationToken)
            ?? throw ExcecaoAplicacao.NaoEncontrado(
                "vistoria_nao_encontrada",
                "Vistoria de entrada não encontrada.");

        await AuditarLeituraAsync(
            ator,
            atendimentoId,
            TiposEventoAtendimento.VistoriaEntradaLida,
            TiposAuditoriaNegocio.VistoriaLida,
            correlacao,
            cancellationToken);

        return new LeituraSensivel(
            _criptografia.Decifrar(
                PropositosCriptografia.VistoriaEntrada,
                atendimentoId,
                vistoria.TextoCifrado));
    }

    /// <summary>
    /// Confirma a auditoria antes de permitir a decifragem. A falha de
    /// gravação propaga e impede a resposta; nenhum conteúdo é registrado.
    /// </summary>
    private async Task AuditarLeituraAsync(
        AtorAtual ator,
        Guid atendimentoId,
        string tipoEvento,
        string tipoAuditoria,
        string? correlacao,
        CancellationToken cancellationToken)
    {
        var agora = _relogio.UtcNow;

        _db.EventosAtendimento.Add(new EventoAtendimento
        {
            AtendimentoId = atendimentoId,
            Tipo = tipoEvento,
            AtorOpaco = ator.Id.ToString(),
            OcorridoEmUtc = agora,
            Correlacao = correlacao
        });

        await _auditoria.RegistrarAsync(
            new EventoAuditoriaNegocio(
                tipoAuditoria,
                ator.Id,
                "atendimento",
                atendimentoId,
                agora,
                correlacao,
                null),
            cancellationToken);
    }
}
