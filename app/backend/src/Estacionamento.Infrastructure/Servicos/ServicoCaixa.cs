using System.Data;
using Estacionamento.Application.Atendimentos;
using Estacionamento.Application.Auditoria;
using Estacionamento.Application.Caixa;
using Estacionamento.Application.Comum;
using Estacionamento.Application.Idempotencia;
using Estacionamento.Application.Seguranca;
using Estacionamento.Application.Tempo;
using Estacionamento.Domain.Atendimentos;
using Estacionamento.Domain.Idempotencia;
using Estacionamento.Domain.Retiradas;
using Estacionamento.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Estacionamento.Infrastructure.Servicos;

/// <summary>
/// Acerto integral, desconto e estorno serializados pelo cabeçalho financeiro
/// do atendimento. Cada comando bloqueia a razão com <c>UPDLOCK, HOLDLOCK</c>,
/// relê os lançamentos, confere a revisão esperada e confirma lançamento,
/// revisão, eventos, auditoria e resultado idempotente na mesma transação.
/// </summary>
public sealed class ServicoCaixa : IServicoCaixa
{
    private const int TamanhoMaximoJustificativa = 500;

    private readonly EstacionamentoDbContext _db;
    private readonly ICriptografiaCampos _criptografia;
    private readonly IAuditoriaNegocio _auditoria;
    private readonly IRelogio _relogio;

    public ServicoCaixa(
        EstacionamentoDbContext db,
        ICriptografiaCampos criptografia,
        IAuditoriaNegocio auditoria,
        IRelogio relogio)
    {
        _db = db;
        _criptografia = criptografia;
        _auditoria = auditoria;
        _relogio = relogio;
    }

    public async Task<ResultadoLancamento> RegistrarPagamentoAsync(
        AtorAtual ator,
        Guid pedidoId,
        RequisicaoPagamento requisicao,
        Guid chaveIdempotente,
        string? correlacao,
        CancellationToken cancellationToken = default)
    {
        if (!ator.EhOperador)
        {
            throw ExcecaoAplicacao.AcessoNegado();
        }

        ApoioIdempotenciaFase3.GarantirChave(chaveIdempotente);
        ApoioIdempotenciaFase3.GarantirValorMonetario(
            requisicao.Valor,
            "pagamento_invalido",
            "Informe um valor positivo com no máximo duas casas decimais.");

        var forma = requisicao.Forma?.Trim() ?? string.Empty;
        if (!FormaPagamento.EhValida(forma))
        {
            throw ExcecaoAplicacao.Validacao(
                "payment_invalid_method",
                "A forma de pagamento deve ser dinheiro, cartão ou Pix.");
        }

        if (requisicao.RevisaoEsperada < 1)
        {
            throw ExcecaoAplicacao.Validacao(
                "ledger_revision_stale",
                "Informe a revisão atual da razão do atendimento.");
        }

        var hash = CanonicalizadorIdempotencia.CalcularHashPagamentoV1(
            ator.Id,
            pedidoId,
            requisicao.Valor,
            forma,
            requisicao.RevisaoEsperada);

        await using var transacao = await _db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        var repeticao = await BuscarRepeticaoAsync(
            ator,
            ResultadoIdempotente.OperacaoPagamento,
            chaveIdempotente,
            hash,
            cancellationToken);

        if (repeticao is not null)
        {
            return repeticao;
        }

        var agora = _relogio.UtcNow;
        var reserva = NovaReserva(
            ator,
            ResultadoIdempotente.OperacaoPagamento,
            chaveIdempotente,
            hash,
            agora);

        var replayReserva = await ReservarAsync(reserva, ator, hash, cancellationToken);
        if (replayReserva is not null)
        {
            return replayReserva;
        }

        try
        {
            var contexto = await CarregarContextoAsync(
                pedidoId,
                requisicao.RevisaoEsperada,
                cancellationToken);

            if (contexto.Composicao.Saldo == 0)
            {
                throw ExcecaoAplicacao.RegraNaoSatisfeita(
                    "balance_already_settled",
                    "A cobrança já está zerada e não exige pagamento.");
            }

            if (requisicao.Valor != contexto.Composicao.Saldo)
            {
                throw ExcecaoAplicacao.RegraNaoSatisfeita(
                    "payment_must_equal_balance",
                    "O acerto deve ser integral: o valor precisa ser exatamente igual ao saldo exigível.");
            }

            var lancamento = new LancamentoFinanceiro
            {
                Id = Guid.NewGuid(),
                AtendimentoId = contexto.Pedido.AtendimentoId,
                Tipo = TipoLancamentoFinanceiro.Pagamento,
                Valor = requisicao.Valor,
                FormaPagamento = forma,
                AtorId = ator.Id,
                OcorridoEmUtc = agora
            };

            var resultado = await RegistrarLancamentoAsync(
                ator,
                contexto,
                lancamento,
                agora,
                correlacao,
                TiposEventoAtendimento.PagamentoRegistrado,
                TiposAuditoriaNegocio.PagamentoRegistrado,
                cancellationToken);

            reserva.ResultadoJson = ApoioIdempotenciaFase3.Serializar(resultado);
            reserva.CodigoResposta = 201;

            await _db.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);

            return resultado;
        }
        catch (DbUpdateException excecao) when (ErrosSql.EhViolacaoUnicidade(excecao))
        {
            await transacao.RollbackAsync(cancellationToken);
            _db.ChangeTracker.Clear();

            var confirmado = await BuscarRepeticaoAsync(
                ator,
                ResultadoIdempotente.OperacaoPagamento,
                chaveIdempotente,
                hash,
                cancellationToken);

            return confirmado ?? throw ExcecaoAplicacao.Conflito(
                "idempotency_conflict",
                "A mesma chave idempotente foi reutilizada com outro conteúdo.");
        }
    }

    public async Task<ResultadoLancamento> RegistrarDescontoAsync(
        AtorAtual ator,
        Guid pedidoId,
        RequisicaoDesconto requisicao,
        Guid chaveIdempotente,
        string? correlacao,
        CancellationToken cancellationToken = default)
    {
        if (!ator.EhAdministrador)
        {
            throw ExcecaoAplicacao.AcessoNegado();
        }

        ApoioIdempotenciaFase3.GarantirChave(chaveIdempotente);
        ApoioIdempotenciaFase3.GarantirValorMonetario(
            requisicao.Valor,
            "desconto_invalido",
            "O desconto deve ser um valor positivo com no máximo duas casas decimais.");

        var justificativa = ApoioIdempotenciaFase3.NormalizarTextoObrigatorio(
            requisicao.Justificativa,
            TamanhoMaximoJustificativa,
            "discount_requires_justification",
            "Informe a justificativa obrigatória do desconto.");

        if (requisicao.RevisaoEsperada < 1)
        {
            throw ExcecaoAplicacao.Validacao(
                "ledger_revision_stale",
                "Informe a revisão atual da razão do atendimento.");
        }

        var hash = CanonicalizadorIdempotencia.CalcularHashDescontoV1(
            ator.Id,
            pedidoId,
            requisicao.Valor,
            justificativa,
            requisicao.RevisaoEsperada);

        await using var transacao = await _db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        var repeticao = await BuscarRepeticaoAsync(
            ator,
            ResultadoIdempotente.OperacaoDesconto,
            chaveIdempotente,
            hash,
            cancellationToken);

        if (repeticao is not null)
        {
            return repeticao;
        }

        var agora = _relogio.UtcNow;
        var reserva = NovaReserva(
            ator,
            ResultadoIdempotente.OperacaoDesconto,
            chaveIdempotente,
            hash,
            agora);

        var replayReserva = await ReservarAsync(reserva, ator, hash, cancellationToken);
        if (replayReserva is not null)
        {
            return replayReserva;
        }

        try
        {
            var contexto = await CarregarContextoAsync(
                pedidoId,
                requisicao.RevisaoEsperada,
                cancellationToken);

            if (contexto.Composicao.PagoLiquido > 0)
            {
                throw ExcecaoAplicacao.RegraNaoSatisfeita(
                    "discount_requires_no_payment",
                    "Desconto bloqueado: existe pagamento líquido maior que zero. Um estorno integral pode reabrir a possibilidade.");
            }

            if (requisicao.Valor > contexto.Composicao.Cobrado)
            {
                throw ExcecaoAplicacao.RegraNaoSatisfeita(
                    "discount_exceeds_balance",
                    "O desconto não pode exceder o valor ainda descontável do atendimento.");
            }

            var lancamentoId = Guid.NewGuid();
            var lancamento = new LancamentoFinanceiro
            {
                Id = lancamentoId,
                AtendimentoId = contexto.Pedido.AtendimentoId,
                Tipo = TipoLancamentoFinanceiro.Desconto,
                Valor = requisicao.Valor,
                JustificativaCifrada = _criptografia.CifrarComRegistro(
                    PropositosCriptografia.JustificativaDesconto,
                    contexto.Pedido.AtendimentoId,
                    lancamentoId,
                    justificativa),
                AtorId = ator.Id,
                OcorridoEmUtc = agora
            };

            var resultado = await RegistrarLancamentoAsync(
                ator,
                contexto,
                lancamento,
                agora,
                correlacao,
                TiposEventoAtendimento.DescontoConcedido,
                TiposAuditoriaNegocio.DescontoConcedido,
                cancellationToken);

            reserva.ResultadoJson = ApoioIdempotenciaFase3.Serializar(resultado);
            reserva.CodigoResposta = 201;

            await _db.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);

            return resultado;
        }
        catch (DbUpdateException excecao) when (ErrosSql.EhViolacaoUnicidade(excecao))
        {
            await transacao.RollbackAsync(cancellationToken);
            _db.ChangeTracker.Clear();

            var confirmado = await BuscarRepeticaoAsync(
                ator,
                ResultadoIdempotente.OperacaoDesconto,
                chaveIdempotente,
                hash,
                cancellationToken);

            return confirmado ?? throw ExcecaoAplicacao.Conflito(
                "idempotency_conflict",
                "A mesma chave idempotente foi reutilizada com outro conteúdo.");
        }
    }

    public async Task<ResultadoLancamento> RegistrarEstornoAsync(
        AtorAtual ator,
        Guid pedidoId,
        RequisicaoEstorno requisicao,
        Guid chaveIdempotente,
        string? correlacao,
        CancellationToken cancellationToken = default)
    {
        if (!ator.EhAdministrador)
        {
            throw ExcecaoAplicacao.AcessoNegado();
        }

        ApoioIdempotenciaFase3.GarantirChave(chaveIdempotente);
        ApoioIdempotenciaFase3.GarantirValorMonetario(
            requisicao.Valor,
            "estorno_invalido",
            "Informe o valor integral positivo do pagamento a estornar.");

        var justificativa = ApoioIdempotenciaFase3.NormalizarTextoObrigatorio(
            requisicao.Justificativa,
            TamanhoMaximoJustificativa,
            "estorno_requires_justification",
            "Informe a justificativa obrigatória do estorno.");

        if (requisicao.PagamentoId == Guid.Empty)
        {
            throw ExcecaoAplicacao.Validacao(
                "payment_not_found",
                "Selecione o pagamento ainda não estornado.");
        }

        if (requisicao.RevisaoEsperada < 1)
        {
            throw ExcecaoAplicacao.Validacao(
                "ledger_revision_stale",
                "Informe a revisão atual da razão do atendimento.");
        }

        var hash = CanonicalizadorIdempotencia.CalcularHashEstornoV1(
            ator.Id,
            pedidoId,
            requisicao.PagamentoId,
            requisicao.Valor,
            justificativa,
            requisicao.RevisaoEsperada);

        await using var transacao = await _db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        var repeticao = await BuscarRepeticaoAsync(
            ator,
            ResultadoIdempotente.OperacaoEstorno,
            chaveIdempotente,
            hash,
            cancellationToken);

        if (repeticao is not null)
        {
            return repeticao;
        }

        var agora = _relogio.UtcNow;
        var reserva = NovaReserva(
            ator,
            ResultadoIdempotente.OperacaoEstorno,
            chaveIdempotente,
            hash,
            agora);

        var replayReserva = await ReservarAsync(reserva, ator, hash, cancellationToken);
        if (replayReserva is not null)
        {
            return replayReserva;
        }

        try
        {
            var contexto = await CarregarContextoAsync(
                pedidoId,
                requisicao.RevisaoEsperada,
                cancellationToken);

            var pagamento = contexto.Lancamentos
                .SingleOrDefault(lancamento =>
                    lancamento.Id == requisicao.PagamentoId &&
                    lancamento.Tipo == TipoLancamentoFinanceiro.Pagamento)
                ?? throw ExcecaoAplicacao.NaoEncontrado(
                    "payment_not_found",
                    "Pagamento não encontrado para este atendimento.");

            if (requisicao.Valor != pagamento.Valor)
            {
                throw ExcecaoAplicacao.RegraNaoSatisfeita(
                    "refund_must_be_full",
                    "O estorno deve ser integral e igual ao pagamento original.");
            }

            var jaEstornado = contexto.Lancamentos.Any(lancamento =>
                lancamento.Tipo == TipoLancamentoFinanceiro.Estorno &&
                lancamento.PagamentoOriginalId == pagamento.Id);

            if (jaEstornado)
            {
                throw ExcecaoAplicacao.Conflito(
                    "payment_already_refunded",
                    "Este pagamento já foi estornado. O estorno original permanece preservado.");
            }

            var lancamentoId = Guid.NewGuid();
            var lancamento = new LancamentoFinanceiro
            {
                Id = lancamentoId,
                AtendimentoId = contexto.Pedido.AtendimentoId,
                Tipo = TipoLancamentoFinanceiro.Estorno,
                Valor = requisicao.Valor,
                PagamentoOriginalId = pagamento.Id,
                JustificativaCifrada = _criptografia.CifrarComRegistro(
                    PropositosCriptografia.JustificativaEstorno,
                    contexto.Pedido.AtendimentoId,
                    lancamentoId,
                    justificativa),
                AtorId = ator.Id,
                OcorridoEmUtc = agora
            };

            var resultado = await RegistrarLancamentoAsync(
                ator,
                contexto,
                lancamento,
                agora,
                correlacao,
                TiposEventoAtendimento.EstornoRegistrado,
                TiposAuditoriaNegocio.EstornoRegistrado,
                cancellationToken);

            reserva.ResultadoJson = ApoioIdempotenciaFase3.Serializar(resultado);
            reserva.CodigoResposta = 201;

            await _db.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);

            return resultado;
        }
        catch (DbUpdateException excecao) when (ErrosSql.EhViolacaoUnicidade(excecao))
        {
            await transacao.RollbackAsync(cancellationToken);
            _db.ChangeTracker.Clear();

            var confirmado = await BuscarRepeticaoAsync(
                ator,
                ResultadoIdempotente.OperacaoEstorno,
                chaveIdempotente,
                hash,
                cancellationToken);

            if (confirmado is not null)
            {
                return confirmado;
            }

            throw ExcecaoAplicacao.Conflito(
                "payment_already_refunded",
                "Este pagamento já foi estornado. O estorno original permanece preservado.");
        }
    }

    public async Task<LeituraSensivel> LerJustificativaLancamentoAsync(
        AtorAtual ator,
        Guid pedidoId,
        Guid lancamentoId,
        string? correlacao,
        CancellationToken cancellationToken = default)
    {
        if (!ator.EhAdministrador)
        {
            throw ExcecaoAplicacao.AcessoNegado("authorization_justification_denied");
        }

        var pedido = await _db.PedidosRetirada
            .AsNoTracking()
            .SingleOrDefaultAsync(registro => registro.Id == pedidoId, cancellationToken)
            ?? throw ExcecaoAplicacao.NaoEncontrado(
                "withdrawal_not_found",
                "Pedido de retirada não encontrado.");

        var lancamento = await _db.LancamentosFinanceiros
            .AsNoTracking()
            .SingleOrDefaultAsync(
                registro =>
                    registro.Id == lancamentoId &&
                    registro.AtendimentoId == pedido.AtendimentoId,
                cancellationToken)
            ?? throw ExcecaoAplicacao.NaoEncontrado(
                "launch_not_found",
                "Lançamento financeiro não encontrado.");

        if (lancamento.JustificativaCifrada is null ||
            lancamento.Tipo is not (TipoLancamentoFinanceiro.Desconto or TipoLancamentoFinanceiro.Estorno))
        {
            throw ExcecaoAplicacao.RegraNaoSatisfeita(
                "launch_without_justification",
                "Este lançamento não possui justificativa administrativa.");
        }

        var proposito = lancamento.Tipo == TipoLancamentoFinanceiro.Desconto
            ? PropositosCriptografia.JustificativaDesconto
            : PropositosCriptografia.JustificativaEstorno;

        await AuditarLeituraAsync(
            ator,
            pedido.AtendimentoId,
            lancamento.Id,
            correlacao,
            cancellationToken);

        return new LeituraSensivel(
            _criptografia.DecifrarComRegistro(
                proposito,
                pedido.AtendimentoId,
                lancamento.Id,
                lancamento.JustificativaCifrada));
    }

    private async Task<ResultadoLancamento?> BuscarRepeticaoAsync(
        AtorAtual ator,
        string operacao,
        Guid chaveIdempotente,
        byte[] hash,
        CancellationToken cancellationToken)
    {
        var repeticao = await _db.ResultadosIdempotentes
            .AsNoTracking()
            .SingleOrDefaultAsync(
                resultado =>
                    resultado.AtorId == ator.Id &&
                    resultado.Operacao == operacao &&
                    resultado.Chave == chaveIdempotente,
                cancellationToken);

        if (repeticao is null)
        {
            return null;
        }

        ApoioIdempotenciaFase3.VerificarHash(repeticao, hash);
        return ApoioIdempotenciaFase3.Ler<ResultadoLancamento>(repeticao);
    }

    private static ResultadoIdempotente NovaReserva(
        AtorAtual ator,
        string operacao,
        Guid chave,
        byte[] hash,
        DateTimeOffset agora) =>
        new()
        {
            Id = Guid.NewGuid(),
            AtorId = ator.Id,
            Operacao = operacao,
            Chave = chave,
            VersaoCanonicalizacao = CanonicalizadorIdempotencia.VersaoAtual,
            HashSha256 = hash,
            CriadoEmUtc = agora
        };

    private async Task<ResultadoLancamento?> ReservarAsync(
        ResultadoIdempotente reserva,
        AtorAtual ator,
        byte[] hash,
        CancellationToken cancellationToken)
    {
        _db.ResultadosIdempotentes.Add(reserva);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateException excecao) when (ErrosSql.EhViolacaoUnicidade(excecao))
        {
            _db.ChangeTracker.Clear();

            var confirmado = await _db.ResultadosIdempotentes
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    resultado =>
                        resultado.AtorId == ator.Id &&
                        resultado.Operacao == reserva.Operacao &&
                        resultado.Chave == reserva.Chave,
                    cancellationToken);

            if (confirmado is null)
            {
                throw ExcecaoAplicacao.Conflito(
                    "idempotency_conflict",
                    "A mesma chave idempotente está em processamento com outro conteúdo.");
            }

            ApoioIdempotenciaFase3.VerificarHash(confirmado, hash);
            return ApoioIdempotenciaFase3.Ler<ResultadoLancamento>(confirmado);
        }
    }

    private async Task<ContextoFinanceiro> CarregarContextoAsync(
        Guid pedidoId,
        int revisaoEsperada,
        CancellationToken cancellationToken)
    {
        var pedido = await _db.PedidosRetirada
            .AsNoTracking()
            .SingleOrDefaultAsync(registro => registro.Id == pedidoId, cancellationToken)
            ?? throw ExcecaoAplicacao.NaoEncontrado(
                "withdrawal_not_found",
                "Pedido de retirada não encontrado.");

        var razao = await _db.RazoesAtendimento
            .FromSqlInterpolated(
                $"SELECT * FROM [RazoesAtendimento] WITH (UPDLOCK, HOLDLOCK) WHERE [AtendimentoId] = {pedido.AtendimentoId}")
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw ExcecaoAplicacao.RegraNaoSatisfeita(
                "ledger_not_found",
                "O atendimento não possui razão financeira.");

        if (razao.Revisao != revisaoEsperada)
        {
            throw ExcecaoAplicacao.Conflito(
                "ledger_revision_stale",
                "A razão do atendimento foi alterada por outra operação. Recarregue o saldo e a revisão atuais.");
        }

        var cobranca = await _db.Cobrancas
            .AsNoTracking()
            .SingleAsync(registro => registro.PedidoId == pedidoId, cancellationToken);

        var lancamentos = await _db.LancamentosFinanceiros
            .Where(registro => registro.AtendimentoId == pedido.AtendimentoId)
            .ToListAsync(cancellationToken);

        var composicao = ComposicaoSaldo.Calcular(cobranca.TotalOriginal, lancamentos);

        return new ContextoFinanceiro(pedido, razao, cobranca, lancamentos, composicao);
    }

    private async Task<ResultadoLancamento> RegistrarLancamentoAsync(
        AtorAtual ator,
        ContextoFinanceiro contexto,
        LancamentoFinanceiro lancamento,
        DateTimeOffset agora,
        string? correlacao,
        string tipoEvento,
        string tipoAuditoria,
        CancellationToken cancellationToken)
    {
        var composicaoFinal = ComposicaoSaldo.Calcular(
            contexto.Cobranca.TotalOriginal,
            contexto.Lancamentos.Append(lancamento));

        _db.LancamentosFinanceiros.Add(lancamento);
        contexto.Razao.Revisao += 1;

        _db.EventosAtendimento.Add(new EventoAtendimento
        {
            AtendimentoId = contexto.Pedido.AtendimentoId,
            Tipo = tipoEvento,
            AtorOpaco = ator.Id.ToString(),
            OcorridoEmUtc = agora,
            Correlacao = correlacao
        });

        _auditoria.Adicionar(new EventoAuditoriaNegocio(
            tipoAuditoria,
            ator.Id,
            "lancamento-financeiro",
            lancamento.Id,
            agora,
            correlacao,
            null));

        return new ResultadoLancamento(
            contexto.Pedido.Id,
            contexto.Pedido.AtendimentoId,
            lancamento.Id,
            lancamento.Tipo,
            lancamento.Valor,
            contexto.Razao.Revisao,
            composicaoFinal.Saldo,
            $"/api/v1/withdrawals/{contexto.Pedido.Id}/cashier");
    }

    private async Task AuditarLeituraAsync(
        AtorAtual ator,
        Guid atendimentoId,
        Guid lancamentoId,
        string? correlacao,
        CancellationToken cancellationToken)
    {
        var agora = _relogio.UtcNow;

        _db.EventosAtendimento.Add(new EventoAtendimento
        {
            AtendimentoId = atendimentoId,
            Tipo = TiposEventoAtendimento.JustificativaAdministrativaLida,
            AtorOpaco = ator.Id.ToString(),
            OcorridoEmUtc = agora,
            Correlacao = correlacao
        });

        await _auditoria.RegistrarAsync(
            new EventoAuditoriaNegocio(
                TiposAuditoriaNegocio.JustificativaLida,
                ator.Id,
                "lancamento-financeiro",
                lancamentoId,
                agora,
                correlacao,
                null),
            cancellationToken);
    }

    private sealed record ContextoFinanceiro(
        PedidoRetirada Pedido,
        RazaoAtendimento Razao,
        Cobranca Cobranca,
        List<LancamentoFinanceiro> Lancamentos,
        ResultadoComposicaoSaldo Composicao);
}
