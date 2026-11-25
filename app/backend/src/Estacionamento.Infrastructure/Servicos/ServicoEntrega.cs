using System.Data;
using System.Diagnostics;
using Estacionamento.Application.Auditoria;
using Estacionamento.Application.Comum;
using Estacionamento.Application.Entrega;
using Estacionamento.Application.Idempotencia;
using Estacionamento.Application.Tempo;
using Estacionamento.Domain.Atendimentos;
using Estacionamento.Domain.Entrega;
using Estacionamento.Domain.Idempotencia;
using Estacionamento.Domain.Retiradas;
using Estacionamento.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Estacionamento.Infrastructure.Servicos;

/// <summary>
/// Ponto único de entrega. A prontidão é apenas informativa; a confirmação
/// reautoriza, procura o replay, reserva a chave e, sob os bloqueios na ordem
/// atendimento → pedido → razão → controles, valida vistoria, conferência,
/// saldo, ocupação própria e versões antes de gravar entrega, consumos,
/// encerramentos, fim da ocupação, histórico, auditoria e recibo juntos.
/// </summary>
public sealed class ServicoEntrega : IServicoEntrega
{
    private readonly EstacionamentoDbContext _db;
    private readonly IAuditoriaNegocio _auditoria;
    private readonly IRelogio _relogio;
    private readonly DbContextOptions<EstacionamentoDbContext> _opcoesContexto;
    private readonly ILogger<ServicoEntrega> _logger;

    public ServicoEntrega(
        EstacionamentoDbContext db,
        IAuditoriaNegocio auditoria,
        IRelogio relogio,
        DbContextOptions<EstacionamentoDbContext> opcoesContexto,
        ILogger<ServicoEntrega> logger)
    {
        _db = db;
        _auditoria = auditoria;
        _relogio = relogio;
        _opcoesContexto = opcoesContexto;
        _logger = logger;
    }

    public async Task<ProntidaoEntrega> ObterProntidaoAsync(
        AtorAtual ator,
        Guid pedidoId,
        CancellationToken cancellationToken = default)
    {
        if (!ator.EhOperador)
        {
            throw ExcecaoAplicacao.AcessoNegado();
        }

        var pedido = await _db.PedidosRetirada
            .AsNoTracking()
            .SingleOrDefaultAsync(registro => registro.Id == pedidoId, cancellationToken)
            ?? throw ExcecaoAplicacao.NaoEncontrado(
                "withdrawal_not_found",
                "Pedido de retirada não encontrado.");

        var atendimento = await _db.Atendimentos
            .AsNoTracking()
            .SingleOrDefaultAsync(registro => registro.Id == pedido.AtendimentoId, cancellationToken)
            ?? throw ExcecaoAplicacao.NaoEncontrado(
                "atendimento_nao_encontrado",
                "Atendimento não encontrado.");

        var entrega = await CarregarResumoEntregaAsync(pedido.Id, cancellationToken);
        var vistoriaRegistrada = await _db.VistoriasSaida
            .AsNoTracking()
            .AnyAsync(vistoria => vistoria.AtendimentoId == atendimento.Id, cancellationToken);

        var ocupacao = await _db.Ocupacoes
            .AsNoTracking()
            .FirstOrDefaultAsync(
                registro => registro.AtendimentoId == atendimento.Id && registro.FimUtc == null,
                cancellationToken);

        var vagaCodigo = await CodigoDaVagaAsync(
            entrega?.VagaId ?? ocupacao?.VagaId,
            cancellationToken);
        var financeiro = await CarregarFinanceiroAsync(pedido, cancellationToken);
        var (documental, financeira) = await CarregarControlesAsync(pedido.Id, cancellationToken);
        var versoes = new VersoesEntrega(
            atendimento.RowVersion,
            pedido.RowVersion,
            financeiro.VersaoRazao,
            documental.Versao,
            financeira.Versao);

        if (entrega is not null ||
            string.Equals(pedido.Estado, EstadoAtendimento.Entregue, StringComparison.Ordinal))
        {
            return new ProntidaoEntrega(
                pedido.Id,
                atendimento.Id,
                atendimento.IdentificadorPublico ?? string.Empty,
                EstadoAtendimento.Entregue,
                vagaCodigo,
                vistoriaRegistrada,
                pedido.Conferencia,
                financeiro.Composicao.Saldo,
                [],
                [CodigosBloqueioEntrega.JaConcluida],
                false,
                entrega,
                versoes);
        }

        var avaliacao = AvaliadorEntrega.Avaliar(
            vistoriaRegistrada,
            pedido.Conferencia,
            financeiro.Composicao.Saldo,
            financeiro.RevisaoRazao,
            documental.Controle,
            financeira.Controle,
            ocupacao is not null);

        return new ProntidaoEntrega(
            pedido.Id,
            atendimento.Id,
            atendimento.IdentificadorPublico ?? string.Empty,
            pedido.Estado,
            vagaCodigo,
            vistoriaRegistrada,
            pedido.Conferencia,
            financeiro.Composicao.Saldo,
            avaliacao.Condicoes,
            avaliacao.Bloqueios,
            avaliacao.Pronta,
            null,
            versoes);
    }

    public async Task<ResultadoEntrega> ConfirmarAsync(
        AtorAtual ator,
        Guid pedidoId,
        RequisicaoConfirmarEntrega requisicao,
        Guid chaveIdempotente,
        string? correlacao,
        CancellationToken cancellationToken = default)
    {
        if (!ator.EhOperador)
        {
            throw ExcecaoAplicacao.AcessoNegado();
        }

        ApoioIdempotenciaFase3.GarantirChave(chaveIdempotente);

        if (requisicao.Confirmacao != true)
        {
            throw ExcecaoAplicacao.Validacao(
                "delivery_confirmation_required",
                "A entrega exige confirmação explícita.");
        }

        var versaoAtendimento = requisicao.VersaoAtendimento ?? [];
        var versaoPedido = requisicao.VersaoPedido ?? [];
        var versaoRazao = requisicao.VersaoRazao ?? [];
        var versaoDocumental = requisicao.VersaoControleDocumental ?? string.Empty;
        var versaoFinanceira = requisicao.VersaoControleFinanceira ?? string.Empty;

        if (versaoAtendimento.Length == 0 ||
            versaoPedido.Length == 0 ||
            versaoRazao.Length == 0 ||
            versaoDocumental.Length == 0 ||
            versaoFinanceira.Length == 0)
        {
            throw ExcecaoAplicacao.Validacao(
                "delivery_invalid_versions",
                "Recarregue a prontidão e informe todas as versões-base da entrega.");
        }

        // Vínculo inicial sem bloqueio de atualização; é revalidado depois da
        // aquisição ordenada dos bloqueios.
        var atendimentoVinculado = await _db.PedidosRetirada
            .AsNoTracking()
            .Where(pedido => pedido.Id == pedidoId)
            .Select(pedido => (Guid?)pedido.AtendimentoId)
            .SingleOrDefaultAsync(cancellationToken);

        var atendimentoId = atendimentoVinculado
            ?? throw ExcecaoAplicacao.NaoEncontrado(
                "withdrawal_not_found",
                "Pedido de retirada não encontrado.");

        var hash = CanonicalizadorIdempotencia.CalcularHashEntregaV1(
            ator.Id,
            pedidoId,
            atendimentoId,
            true,
            versaoAtendimento,
            versaoPedido,
            versaoRazao,
            versaoDocumental,
            versaoFinanceira);

        var cronometro = Stopwatch.StartNew();

        await using var transacao = await _db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        var repeticao = await BuscarRepeticaoAsync(ator, chaveIdempotente, hash, cancellationToken);
        if (repeticao is not null)
        {
            RegistrarDiagnostico(correlacao, "replay", null, cronometro);
            return repeticao;
        }

        var agora = _relogio.UtcNow;
        var reserva = new ResultadoIdempotente
        {
            Id = Guid.NewGuid(),
            AtorId = ator.Id,
            Operacao = ResultadoIdempotente.OperacaoEntrega,
            Chave = chaveIdempotente,
            VersaoCanonicalizacao = CanonicalizadorIdempotencia.VersaoAtual,
            HashSha256 = hash,
            CriadoEmUtc = agora
        };

        _db.ResultadosIdempotentes.Add(reserva);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException excecao) when (ErrosSql.EhViolacaoUnicidade(excecao))
        {
            await transacao.RollbackAsync(cancellationToken);
            _db.ChangeTracker.Clear();

            var confirmado = await BuscarRepeticaoAsync(ator, chaveIdempotente, hash, cancellationToken);
            if (confirmado is not null)
            {
                RegistrarDiagnostico(correlacao, "replay_concorrente", null, cronometro);
                return confirmado;
            }

            RegistrarDiagnostico(correlacao, "conflito_chave", "idempotency_conflict", cronometro);
            throw ExcecaoAplicacao.Conflito(
                "idempotency_conflict",
                "A mesma chave idempotente está em processamento com outro conteúdo.");
        }

        try
        {
            var atendimento = await _db.Atendimentos
                .FromSqlInterpolated(
                    $"SELECT * FROM [Atendimentos] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {atendimentoId}")
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw ExcecaoAplicacao.NaoEncontrado(
                    "atendimento_nao_encontrado",
                    "Atendimento não encontrado.");

            // Ordem de bloqueio: atendimento → pedido → cabeçalho da razão →
            // controles documental/financeiro.
            var pedido = await _db.PedidosRetirada
                .FromSqlInterpolated(
                    $"SELECT * FROM [PedidosRetirada] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {pedidoId}")
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw ExcecaoAplicacao.NaoEncontrado(
                    "withdrawal_not_found",
                    "Pedido de retirada não encontrado.");

            if (pedido.AtendimentoId != atendimento.Id)
            {
                throw ExcecaoAplicacao.Conflito(
                    "concurrency_conflict",
                    "O vínculo entre pedido e atendimento mudou. Recarregue o estado atual.");
            }

            if (!atendimento.RowVersion.SequenceEqual(versaoAtendimento) ||
                !pedido.RowVersion.SequenceEqual(versaoPedido))
            {
                throw ExcecaoAplicacao.Conflito(
                    "concurrency_conflict",
                    "O atendimento ou o pedido foi atualizado por outra operação. Recarregue o estado atual.");
            }

            var entregaExistente = await _db.EntregasVeiculo
                .AsNoTracking()
                .AnyAsync(entrega => entrega.PedidoId == pedido.Id, cancellationToken);

            if (entregaExistente ||
                string.Equals(pedido.Estado, EstadoAtendimento.Entregue, StringComparison.Ordinal) ||
                string.Equals(atendimento.Estado, EstadoAtendimento.Entregue, StringComparison.Ordinal))
            {
                throw ExcecaoAplicacao.Conflito(
                    CodigosBloqueioEntrega.JaConcluida,
                    "O pedido já foi entregue. Nenhuma nova entrega ou consumo é permitido.");
            }

            if (!EstadoAtendimento.ProduzidosNaFase3.Contains(pedido.Estado) ||
                !EstadoAtendimento.ProduzidosNaFase3.Contains(atendimento.Estado))
            {
                throw ExcecaoAplicacao.Conflito(
                    "delivery_state_invalid",
                    "O atendimento e o pedido precisam estar aguardando ou em busca para a entrega.");
            }

            var vistoria = await _db.VistoriasSaida
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    registro => registro.AtendimentoId == atendimento.Id,
                    cancellationToken)
                ?? throw ExcecaoAplicacao.RegraNaoSatisfeita(
                    CodigosBloqueioEntrega.RequerVistoria,
                    "Registre a vistoria de saída antes de confirmar a entrega.");

            var ocupacao = await _db.Ocupacoes
                .FromSqlInterpolated(
                    $"SELECT * FROM [Ocupacoes] WITH (UPDLOCK, HOLDLOCK) WHERE [AtendimentoId] = {atendimento.Id} AND [FimUtc] IS NULL")
                .AsNoTracking()
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw ExcecaoAplicacao.RegraNaoSatisfeita(
                    CodigosBloqueioEntrega.RequerOcupacao,
                    "Não há ocupação aberta vinculada a este atendimento.");

            var financeiro = await CarregarFinanceiroComBloqueioAsync(pedido, cancellationToken);
            if (!financeiro.VersaoRazao.SequenceEqual(versaoRazao))
            {
                throw ExcecaoAplicacao.Conflito(
                    "concurrency_conflict",
                    "A razão do atendimento foi alterada por outra operação. Recarregue o saldo e a revisão atuais.");
            }

            var (documental, financeira) = await CarregarControlesComBloqueioAsync(
                pedido.Id,
                cancellationToken);

            if (!VersaoControleAutorizacao.Corresponde(
                    versaoDocumental,
                    TipoAutorizacaoRetirada.Documental,
                    documental.Concessao?.Id,
                    documental.Concessao?.RevisaoReferencia,
                    documental.Revogada) ||
                !VersaoControleAutorizacao.Corresponde(
                    versaoFinanceira,
                    TipoAutorizacaoRetirada.Financeira,
                    financeira.Concessao?.Id,
                    financeira.Concessao?.RevisaoReferencia,
                    financeira.Revogada))
            {
                throw ExcecaoAplicacao.RegraNaoSatisfeita(
                    CodigosBloqueioEntrega.AutorizacaoObsoleta,
                    "As autorizações mudaram desde a prontidão. Recarregue o estado atual.");
            }

            var avaliacao = AvaliadorEntrega.Avaliar(
                true,
                pedido.Conferencia,
                financeiro.Composicao.Saldo,
                financeiro.RevisaoRazao,
                documental.Controle,
                financeira.Controle,
                true);

            if (!avaliacao.Pronta)
            {
                var condicao = avaliacao.Condicoes.First(condicao => !condicao.Atendida);
                throw ExcecaoAplicacao.RegraNaoSatisfeita(
                    condicao.CodigoBloqueio ?? "delivery_blocked",
                    condicao.Situacao);
            }

            var entregaId = Guid.NewGuid();
            var entrega = new EntregaVeiculo
            {
                Id = entregaId,
                AtendimentoId = atendimento.Id,
                PedidoId = pedido.Id,
                VistoriaSaidaId = vistoria.Id,
                OcupacaoId = ocupacao.Id,
                VagaId = ocupacao.VagaId,
                AtorId = ator.Id,
                EntregueEmUtc = agora
            };

            var consumos = new List<ConsumoAutorizacao>();
            AdicionarConsumo(consumos, entregaId, TipoAutorizacaoRetirada.Documental, avaliacao.DocumentalUsada, agora);
            AdicionarConsumo(consumos, entregaId, TipoAutorizacaoRetirada.Financeira, avaliacao.FinanceiraUsada, agora);

            pedido.Estado = EstadoAtendimento.Entregue;
            atendimento.Estado = EstadoAtendimento.Entregue;

            // Encerramento condicional por identidade completa da ocupação. Se
            // outra operação a encerrou ou a vaga já pertence a outra visita,
            // nenhuma linha é afetada e toda a transação é desfeita.
            var linhas = await _db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [Ocupacoes] SET [FimUtc] = {agora} WHERE [Id] = {ocupacao.Id} AND [AtendimentoId] = {atendimento.Id} AND [VagaId] = {ocupacao.VagaId} AND [FimUtc] IS NULL",
                cancellationToken);

            if (linhas != 1)
            {
                throw ExcecaoAplicacao.Conflito(
                    "concurrency_conflict",
                    "A ocupação foi alterada por outra operação. Recarregue o estado atual.");
            }

            var atorOpaco = ator.Id.ToString();
            _db.EntregasVeiculo.Add(entrega);
            _db.ConsumosAutorizacao.AddRange(consumos);

            foreach (var consumo in consumos)
            {
                _db.EventosAtendimento.Add(new EventoAtendimento
                {
                    AtendimentoId = atendimento.Id,
                    Tipo = TiposEventoAtendimento.AutorizacaoConsumida,
                    AtorOpaco = atorOpaco,
                    OcorridoEmUtc = agora,
                    Correlacao = correlacao
                });

                _auditoria.Adicionar(new EventoAuditoriaNegocio(
                    TiposAuditoriaNegocio.AutorizacaoConsumida,
                    ator.Id,
                    "consumo-autorizacao",
                    consumo.Id,
                    agora,
                    correlacao,
                    null));
            }

            _db.EventosAtendimento.AddRange(
                new EventoAtendimento
                {
                    AtendimentoId = atendimento.Id,
                    Tipo = TiposEventoAtendimento.EntregaConfirmada,
                    AtorOpaco = atorOpaco,
                    OcorridoEmUtc = agora,
                    Correlacao = correlacao,
                    DetalheOpaco = entregaId.ToString()
                },
                new EventoAtendimento
                {
                    AtendimentoId = atendimento.Id,
                    Tipo = TiposEventoAtendimento.VagaLiberada,
                    AtorOpaco = atorOpaco,
                    OcorridoEmUtc = agora,
                    Correlacao = correlacao,
                    DetalheOpaco = ocupacao.VagaId.ToString()
                });

            _auditoria.Adicionar(new EventoAuditoriaNegocio(
                TiposAuditoriaNegocio.EntregaConfirmada,
                ator.Id,
                "entrega-veiculo",
                entregaId,
                agora,
                correlacao,
                null));
            _auditoria.Adicionar(new EventoAuditoriaNegocio(
                TiposAuditoriaNegocio.VagaLiberada,
                ator.Id,
                "entrega-veiculo",
                entregaId,
                agora,
                correlacao,
                null));

            var nomeAtor = await NomeDoAtorAsync(ator.Id, cancellationToken);
            var vagaCodigo = await CodigoDaVagaAsync(ocupacao.VagaId, cancellationToken);
            var liberacao = new ResumoEntrega(
                entregaId,
                nomeAtor,
                agora,
                ocupacao.VagaId,
                vagaCodigo,
                agora);

            var resultado = new ResultadoEntrega(
                entregaId,
                pedido.Id,
                atendimento.Id,
                EstadoAtendimento.Entregue,
                agora,
                nomeAtor,
                liberacao,
                $"/api/v1/withdrawals/{pedido.Id}/delivery");

            reserva.AtendimentoId = atendimento.Id;
            reserva.IdentificadorPublico = atendimento.IdentificadorPublico;
            reserva.VagaId = ocupacao.VagaId;
            reserva.Estado = EstadoAtendimento.Entregue;
            reserva.EntradaUtc = atendimento.EntradaUtc;
            reserva.Location = resultado.Location;
            reserva.ResultadoJson = ApoioIdempotenciaFase3.Serializar(resultado);
            reserva.CodigoResposta = 201;

            await _db.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);

            RegistrarDiagnostico(correlacao, "confirmada", null, cronometro);
            return resultado;
        }
        catch (DbUpdateConcurrencyException)
        {
            await transacao.RollbackAsync(cancellationToken);
            _db.ChangeTracker.Clear();
            RegistrarDiagnostico(correlacao, "conflito", "concurrency_conflict", cronometro);
            throw ExcecaoAplicacao.Conflito(
                "concurrency_conflict",
                "O estado foi alterado por outra operação. Recarregue antes de tentar novamente.");
        }
        catch (DbUpdateException excecao) when (ErrosSql.EhViolacaoUnicidade(excecao))
        {
            await transacao.RollbackAsync(cancellationToken);
            _db.ChangeTracker.Clear();

            var confirmado = await BuscarRepeticaoEmContextoNovoAsync(ator, chaveIdempotente, hash);
            if (confirmado is not null)
            {
                RegistrarDiagnostico(correlacao, "replay_concorrente", null, cronometro);
                return confirmado;
            }

            var entregaConcorrente = await ExisteEntregaAsync(pedidoId);
            RegistrarDiagnostico(
                correlacao,
                "conflito",
                entregaConcorrente ? CodigosBloqueioEntrega.JaConcluida : "idempotency_conflict",
                cronometro);

            if (entregaConcorrente)
            {
                throw ExcecaoAplicacao.Conflito(
                    CodigosBloqueioEntrega.JaConcluida,
                    "O pedido foi entregue por outra operação. Recarregue o estado atual.");
            }

            throw ExcecaoAplicacao.Conflito(
                "idempotency_conflict",
                "A mesma chave idempotente foi reutilizada com outro conteúdo.");
        }
        catch (Exception excecao) when (excecao is ExcecaoAplicacao)
        {
            if (excecao is ExcecaoAplicacao aplicacao)
            {
                RegistrarDiagnostico(correlacao, "recusada", aplicacao.Codigo, cronometro);
            }

            throw;
        }
    }

    private async Task<ResultadoEntrega?> BuscarRepeticaoAsync(
        AtorAtual ator,
        Guid chaveIdempotente,
        byte[] hash,
        CancellationToken cancellationToken)
    {
        var repeticao = await _db.ResultadosIdempotentes
            .AsNoTracking()
            .SingleOrDefaultAsync(
                resultado =>
                    resultado.AtorId == ator.Id &&
                    resultado.Operacao == ResultadoIdempotente.OperacaoEntrega &&
                    resultado.Chave == chaveIdempotente,
                cancellationToken);

        if (repeticao is null)
        {
            return null;
        }

        ApoioIdempotenciaFase3.VerificarHash(repeticao, hash);
        return ApoioIdempotenciaFase3.Ler<ResultadoEntrega>(repeticao);
    }

    private async Task<ResultadoEntrega?> BuscarRepeticaoEmContextoNovoAsync(
        AtorAtual ator,
        Guid chaveIdempotente,
        byte[] hash)
    {
        await using var novo = new EstacionamentoDbContext(_opcoesContexto);
        var repeticao = await novo.ResultadosIdempotentes
            .AsNoTracking()
            .SingleOrDefaultAsync(
                resultado =>
                    resultado.AtorId == ator.Id &&
                    resultado.Operacao == ResultadoIdempotente.OperacaoEntrega &&
                    resultado.Chave == chaveIdempotente);

        if (repeticao is null)
        {
            return null;
        }

        ApoioIdempotenciaFase3.VerificarHash(repeticao, hash);
        return ApoioIdempotenciaFase3.Ler<ResultadoEntrega>(repeticao);
    }

    private async Task<bool> ExisteEntregaAsync(Guid pedidoId)
    {
        await using var novo = new EstacionamentoDbContext(_opcoesContexto);
        return await novo.EntregasVeiculo
            .AsNoTracking()
            .AnyAsync(entrega => entrega.PedidoId == pedidoId);
    }

    private async Task<ResumoEntrega?> CarregarResumoEntregaAsync(
        Guid pedidoId,
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
                where entrega.PedidoId == pedidoId
                select new
                {
                    entrega.Id,
                    usuario.Nome,
                    entrega.EntregueEmUtc,
                    entrega.VagaId,
                    vaga.Codigo,
                    FimUtc = ocupacao.FimUtc
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

    private async Task<Financeiro> CarregarFinanceiroAsync(
        PedidoRetirada pedido,
        CancellationToken cancellationToken)
    {
        var cobranca = await _db.Cobrancas
            .AsNoTracking()
            .SingleOrDefaultAsync(registro => registro.PedidoId == pedido.Id, cancellationToken)
            ?? throw ExcecaoAplicacao.RegraNaoSatisfeita(
                "billing_not_found",
                "O pedido não possui cobrança fixada.");

        var razao = await _db.RazoesAtendimento
            .AsNoTracking()
            .SingleOrDefaultAsync(
                registro => registro.AtendimentoId == pedido.AtendimentoId,
                cancellationToken)
            ?? throw ExcecaoAplicacao.RegraNaoSatisfeita(
                "ledger_not_found",
                "O atendimento não possui razão financeira.");

        var lancamentos = await _db.LancamentosFinanceiros
            .AsNoTracking()
            .Where(registro => registro.AtendimentoId == pedido.AtendimentoId)
            .ToListAsync(cancellationToken);

        return new Financeiro(
            razao.Revisao,
            razao.RowVersion,
            ComposicaoSaldo.Calcular(cobranca.TotalOriginal, lancamentos));
    }

    private async Task<Financeiro> CarregarFinanceiroComBloqueioAsync(
        PedidoRetirada pedido,
        CancellationToken cancellationToken)
    {
        var razao = await _db.RazoesAtendimento
            .FromSqlInterpolated(
                $"SELECT * FROM [RazoesAtendimento] WITH (UPDLOCK, HOLDLOCK) WHERE [AtendimentoId] = {pedido.AtendimentoId}")
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw ExcecaoAplicacao.RegraNaoSatisfeita(
                "ledger_not_found",
                "O atendimento não possui razão financeira.");

        var cobranca = await _db.Cobrancas
            .AsNoTracking()
            .SingleOrDefaultAsync(registro => registro.PedidoId == pedido.Id, cancellationToken)
            ?? throw ExcecaoAplicacao.RegraNaoSatisfeita(
                "billing_not_found",
                "O pedido não possui cobrança fixada.");

        var lancamentos = await _db.LancamentosFinanceiros
            .AsNoTracking()
            .Where(registro => registro.AtendimentoId == pedido.AtendimentoId)
            .ToListAsync(cancellationToken);

        return new Financeiro(
            razao.Revisao,
            razao.RowVersion,
            ComposicaoSaldo.Calcular(cobranca.TotalOriginal, lancamentos));
    }

    private async Task<(ControleProjetado Documental, ControleProjetado Financeira)> CarregarControlesAsync(
        Guid pedidoId,
        CancellationToken cancellationToken)
    {
        var concessoes = await _db.ConcessoesAutorizacao
            .AsNoTracking()
            .Where(concessao => concessao.PedidoId == pedidoId)
            .ToListAsync(cancellationToken);

        var revogacoes = await _db.RevogacoesAutorizacao
            .AsNoTracking()
            .Where(revogacao => concessoes.Select(concessao => concessao.Id).Contains(revogacao.ConcessaoId))
            .Select(revogacao => revogacao.ConcessaoId)
            .ToListAsync(cancellationToken);

        return ProjetarControles(concessoes, revogacoes.ToHashSet());
    }

    private async Task<(ControleProjetado Documental, ControleProjetado Financeira)> CarregarControlesComBloqueioAsync(
        Guid pedidoId,
        CancellationToken cancellationToken)
    {
        var concessoes = await _db.ConcessoesAutorizacao
            .FromSqlInterpolated(
                $"SELECT * FROM [ConcessoesAutorizacao] WITH (UPDLOCK, HOLDLOCK) WHERE [PedidoId] = {pedidoId}")
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var revogacoes = await _db.RevogacoesAutorizacao
            .FromSqlInterpolated(
                $"SELECT * FROM [RevogacoesAutorizacao] WITH (UPDLOCK, HOLDLOCK) WHERE [ConcessaoId] IN (SELECT [Id] FROM [ConcessoesAutorizacao] WHERE [PedidoId] = {pedidoId})")
            .AsNoTracking()
            .Select(revogacao => revogacao.ConcessaoId)
            .ToListAsync(cancellationToken);

        return ProjetarControles(concessoes, revogacoes.ToHashSet());
    }

    private static (ControleProjetado Documental, ControleProjetado Financeira) ProjetarControles(
        IReadOnlyList<ConcessaoAutorizacao> concessoes,
        IReadOnlySet<Guid> revogadas)
    {
        return (
            ProjetarControle(TipoAutorizacaoRetirada.Documental, concessoes, revogadas),
            ProjetarControle(TipoAutorizacaoRetirada.Financeira, concessoes, revogadas));
    }

    private static ControleProjetado ProjetarControle(
        string tipo,
        IReadOnlyList<ConcessaoAutorizacao> concessoes,
        IReadOnlySet<Guid> revogadas)
    {
        var ultima = concessoes
            .Where(concessao => string.Equals(concessao.Tipo, tipo, StringComparison.Ordinal))
            .OrderBy(concessao => concessao.ConcedidaEmUtc)
            .ThenBy(concessao => concessao.Id)
            .LastOrDefault();

        var revogada = ultima is not null && revogadas.Contains(ultima.Id);
        var controle = new ControleAutorizacao(
            ultima?.Id,
            ultima?.RevisaoReferencia,
            revogada);

        return new ControleProjetado(
            ultima,
            revogada,
            controle,
            VersaoControleAutorizacao.Calcular(
                tipo,
                ultima?.Id,
                ultima?.RevisaoReferencia,
                revogada));
    }

    private static void AdicionarConsumo(
        ICollection<ConsumoAutorizacao> consumos,
        Guid entregaId,
        string tipo,
        ControleAutorizacao? controle,
        DateTimeOffset agora)
    {
        if (controle?.Id is not { } concessaoId)
        {
            return;
        }

        consumos.Add(new ConsumoAutorizacao
        {
            Id = Guid.NewGuid(),
            EntregaId = entregaId,
            ConcessaoId = concessaoId,
            Tipo = tipo,
            RevisaoReferencia = controle.RevisaoReferencia ?? 1,
            ConsumidaEmUtc = agora
        });
    }

    private async Task<string> NomeDoAtorAsync(Guid atorId, CancellationToken cancellationToken) =>
        await _db.Users
            .AsNoTracking()
            .Where(usuario => usuario.Id == atorId)
            .Select(usuario => usuario.Nome)
            .SingleAsync(cancellationToken);

    private async Task<string> CodigoDaVagaAsync(Guid? vagaId, CancellationToken cancellationToken)
    {
        if (vagaId is null)
        {
            return string.Empty;
        }

        return await _db.Vagas
            .AsNoTracking()
            .Where(vaga => vaga.Id == vagaId)
            .Select(vaga => vaga.Codigo)
            .SingleOrDefaultAsync(cancellationToken) ?? string.Empty;
    }

    private void RegistrarDiagnostico(
        string? correlacao,
        string resultado,
        string? conflito,
        Stopwatch cronometro)
    {
        _logger.LogInformation(
            "Entrega {Acao}: {Resultado}. Correlação: {Correlacao}. Conflito: {Conflito}. Latência: {LatenciaMs} ms.",
            "withdrawal.delivery",
            resultado,
            correlacao,
            conflito,
            cronometro.ElapsedMilliseconds);
    }

    private sealed record Financeiro(
        int RevisaoRazao,
        byte[] VersaoRazao,
        ResultadoComposicaoSaldo Composicao);

    private sealed record ControleProjetado(
        ConcessaoAutorizacao? Concessao,
        bool Revogada,
        ControleAutorizacao Controle,
        string Versao);
}
