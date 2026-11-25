using System.Data;
using Estacionamento.Application.Atendimentos;
using Estacionamento.Application.Auditoria;
using Estacionamento.Application.Caixa;
using Estacionamento.Application.Comum;
using Estacionamento.Application.Idempotencia;
using Estacionamento.Application.Seguranca;
using Estacionamento.Application.Tempo;
using Estacionamento.Domain.Atendimentos;
using Estacionamento.Domain.Entrega;
using Estacionamento.Domain.Idempotencia;
using Estacionamento.Domain.Retiradas;
using Estacionamento.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Estacionamento.Infrastructure.Servicos;

/// <summary>
/// Concessão, revogação e leitura auditada de autorizações de exceção. A
/// vigência é derivada da versão da conferência ou da razão; nenhuma operação
/// desta fase consome a autorização ou altera conferência, saldo e pagamentos.
/// </summary>
public sealed class ServicoAutorizacoes : IServicoAutorizacoes
{
    private const int TamanhoMaximoJustificativa = 500;

    private readonly EstacionamentoDbContext _db;
    private readonly ICriptografiaCampos _criptografia;
    private readonly IAuditoriaNegocio _auditoria;
    private readonly IRelogio _relogio;

    public ServicoAutorizacoes(
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

    public async Task<ResultadoAutorizacao> ConcederAsync(
        AtorAtual ator,
        Guid pedidoId,
        RequisicaoConcessaoAutorizacao requisicao,
        Guid chaveIdempotente,
        string? correlacao,
        CancellationToken cancellationToken = default)
    {
        if (!ator.EhAdministrador)
        {
            throw ExcecaoAplicacao.AcessoNegado();
        }

        ApoioIdempotenciaFase3.GarantirChave(chaveIdempotente);

        var tipo = requisicao.Tipo?.Trim() ?? string.Empty;
        if (!TipoAutorizacaoRetirada.EhValido(tipo))
        {
            throw ExcecaoAplicacao.Validacao(
                "authorization_invalid_type",
                "O tipo de autorização deve ser documental ou financeira.");
        }

        var justificativa = ApoioIdempotenciaFase3.NormalizarTextoObrigatorio(
            requisicao.Justificativa,
            TamanhoMaximoJustificativa,
            "authorization_requires_justification",
            "Informe a justificativa obrigatória da autorização.");

        if (requisicao.VersaoEsperada.Length == 0)
        {
            throw ExcecaoAplicacao.Validacao(
                "stale_withdrawal_version",
                "Informe a versão atual do pedido para conceder a autorização.");
        }

        var hash = CanonicalizadorIdempotencia.CalcularHashConcessaoAutorizacaoV1(
            ator.Id,
            pedidoId,
            tipo,
            requisicao.VersaoEsperada,
            justificativa);

        await using var transacao = await _db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        var repeticao = await BuscarRepeticaoAsync(
            ator,
            ResultadoIdempotente.OperacaoConcederAutorizacao,
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
            ResultadoIdempotente.OperacaoConcederAutorizacao,
            chaveIdempotente,
            hash,
            agora);

        if (await ReservarAsync(reserva, ator, hash, cancellationToken) is { } replayReserva)
        {
            return replayReserva;
        }

        try
        {
            // Ordem de bloqueio comum aos escritores: atendimento → pedido.
            var atendimentoVinculado = await _db.PedidosRetirada
                .AsNoTracking()
                .Where(registro => registro.Id == pedidoId)
                .Select(registro => (Guid?)registro.AtendimentoId)
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw ExcecaoAplicacao.NaoEncontrado(
                    "withdrawal_not_found",
                    "Pedido de retirada não encontrado.");

            var atendimento = await _db.Atendimentos
                .FromSqlInterpolated(
                    $"SELECT * FROM [Atendimentos] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {atendimentoVinculado}")
                .SingleAsync(cancellationToken);

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

            await BloquearMutacaoSeEntregueAsync(pedido, atendimento, cancellationToken);

            if (!pedido.RowVersion.SequenceEqual(requisicao.VersaoEsperada))
            {
                throw ExcecaoAplicacao.Conflito(
                    "stale_withdrawal_version",
                    "O pedido foi atualizado por outra operação. Recarregue o estado atual antes de tentar novamente.");
            }

            var revisaoReferencia = 1;
            if (tipo == TipoAutorizacaoRetirada.Financeira)
            {
                var razao = await _db.RazoesAtendimento
                    .FromSqlInterpolated(
                        $"SELECT * FROM [RazoesAtendimento] WITH (UPDLOCK, HOLDLOCK) WHERE [AtendimentoId] = {pedido.AtendimentoId}")
                    .SingleOrDefaultAsync(cancellationToken)
                    ?? throw ExcecaoAplicacao.RegraNaoSatisfeita(
                        "ledger_not_found",
                        "O atendimento não possui razão financeira.");

                revisaoReferencia = razao.Revisao;
            }

            var concessaoId = Guid.NewGuid();
            var proposito = tipo == TipoAutorizacaoRetirada.Documental
                ? PropositosCriptografia.JustificativaAutorizacaoDocumental
                : PropositosCriptografia.JustificativaAutorizacaoFinanceira;

            var concessao = new ConcessaoAutorizacao
            {
                Id = concessaoId,
                PedidoId = pedido.Id,
                Tipo = tipo,
                RevisaoReferencia = revisaoReferencia,
                JustificativaCifrada = _criptografia.CifrarComRegistro(
                    proposito,
                    pedido.AtendimentoId,
                    concessaoId,
                    justificativa),
                ConcedidaPorId = ator.Id,
                ConcedidaEmUtc = agora
            };

            _db.ConcessoesAutorizacao.Add(concessao);

            _db.EventosAtendimento.Add(new EventoAtendimento
            {
                AtendimentoId = pedido.AtendimentoId,
                Tipo = TiposEventoAtendimento.AutorizacaoConcedida,
                AtorOpaco = ator.Id.ToString(),
                OcorridoEmUtc = agora,
                Correlacao = correlacao
            });

            _auditoria.Adicionar(new EventoAuditoriaNegocio(
                TiposAuditoriaNegocio.AutorizacaoConcedida,
                ator.Id,
                "autorizacao-retirada",
                concessao.Id,
                agora,
                correlacao,
                null));

            var resultado = new ResultadoAutorizacao(
                pedido.Id,
                concessao.Id,
                tipo,
                VigenciaAutorizacao.Vigente,
                revisaoReferencia,
                concessao.ConcedidaEmUtc,
                $"/api/v1/withdrawals/{pedido.Id}/authorizations/{concessao.Id}");

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

            return await BuscarRepeticaoAsync(
                ator,
                ResultadoIdempotente.OperacaoConcederAutorizacao,
                chaveIdempotente,
                hash,
                cancellationToken)
                ?? throw ExcecaoAplicacao.Conflito(
                    "idempotency_conflict",
                    "A mesma chave idempotente foi reutilizada com outro conteúdo.");
        }
    }

    public async Task<ResultadoRevogacao> RevogarAsync(
        AtorAtual ator,
        Guid pedidoId,
        Guid autorizacaoId,
        RequisicaoRevogacaoAutorizacao requisicao,
        Guid chaveIdempotente,
        string? correlacao,
        CancellationToken cancellationToken = default)
    {
        if (!ator.EhAdministrador)
        {
            throw ExcecaoAplicacao.AcessoNegado();
        }

        ApoioIdempotenciaFase3.GarantirChave(chaveIdempotente);

        var justificativa = ApoioIdempotenciaFase3.NormalizarTextoObrigatorio(
            requisicao.Justificativa,
            TamanhoMaximoJustificativa,
            "revocation_requires_justification",
            "Informe a justificativa obrigatória da revogação.");

        var hash = CanonicalizadorIdempotencia.CalcularHashRevogacaoAutorizacaoV1(
            ator.Id,
            pedidoId,
            autorizacaoId,
            justificativa);

        await using var transacao = await _db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        var repeticao = await BuscarRepeticaoRevogacaoAsync(
            ator,
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
            ResultadoIdempotente.OperacaoRevogarAutorizacao,
            chaveIdempotente,
            hash,
            agora);

        if (await ReservarRevogacaoAsync(reserva, ator, hash, cancellationToken) is { } replayReserva)
        {
            return replayReserva;
        }

        try
        {
            var atendimentoVinculado = await _db.PedidosRetirada
                .AsNoTracking()
                .Where(registro => registro.Id == pedidoId)
                .Select(registro => (Guid?)registro.AtendimentoId)
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw ExcecaoAplicacao.NaoEncontrado(
                    "withdrawal_not_found",
                    "Pedido de retirada não encontrado.");

            var atendimento = await _db.Atendimentos
                .FromSqlInterpolated(
                    $"SELECT * FROM [Atendimentos] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {atendimentoVinculado}")
                .SingleAsync(cancellationToken);

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

            await BloquearMutacaoSeEntregueAsync(pedido, atendimento, cancellationToken);

            var concessao = await _db.ConcessoesAutorizacao
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    registro => registro.Id == autorizacaoId && registro.PedidoId == pedidoId,
                    cancellationToken)
                ?? throw ExcecaoAplicacao.NaoEncontrado(
                    "authorization_not_found",
                    "Autorização não encontrada para este pedido.");

            var jaRevogada = await _db.RevogacoesAutorizacao
                .AsNoTracking()
                .AnyAsync(registro => registro.ConcessaoId == concessao.Id, cancellationToken);

            if (jaRevogada)
            {
                throw ExcecaoAplicacao.Conflito(
                    "authorization_already_revoked",
                    "Esta autorização já foi revogada. A concessão e a revogação permanecem no histórico.");
            }

            var revogacaoId = Guid.NewGuid();
            var proposito = concessao.Tipo == TipoAutorizacaoRetirada.Documental
                ? PropositosCriptografia.JustificativaAutorizacaoDocumental
                : PropositosCriptografia.JustificativaAutorizacaoFinanceira;

            var revogacao = new RevogacaoAutorizacao
            {
                Id = revogacaoId,
                ConcessaoId = concessao.Id,
                JustificativaCifrada = _criptografia.CifrarComRegistro(
                    proposito,
                    pedido.AtendimentoId,
                    revogacaoId,
                    justificativa),
                RevogadaPorId = ator.Id,
                RevogadaEmUtc = agora
            };

            _db.RevogacoesAutorizacao.Add(revogacao);

            _db.EventosAtendimento.Add(new EventoAtendimento
            {
                AtendimentoId = pedido.AtendimentoId,
                Tipo = TiposEventoAtendimento.AutorizacaoRevogada,
                AtorOpaco = ator.Id.ToString(),
                OcorridoEmUtc = agora,
                Correlacao = correlacao
            });

            _auditoria.Adicionar(new EventoAuditoriaNegocio(
                TiposAuditoriaNegocio.AutorizacaoRevogada,
                ator.Id,
                "autorizacao-retirada",
                concessao.Id,
                agora,
                correlacao,
                null));

            var resultado = new ResultadoRevogacao(
                pedido.Id,
                concessao.Id,
                revogacaoId,
                revogacao.RevogadaEmUtc);

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

            var confirmado = await BuscarRepeticaoRevogacaoAsync(
                ator,
                chaveIdempotente,
                hash,
                cancellationToken);

            return confirmado ?? throw ExcecaoAplicacao.Conflito(
                "authorization_already_revoked",
                "Esta autorização já foi revogada. A concessão e a revogação permanecem no histórico.");
        }
    }

    public async Task<LeituraSensivel> LerJustificativaAsync(
        AtorAtual ator,
        Guid pedidoId,
        Guid autorizacaoId,
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

        var concessao = await _db.ConcessoesAutorizacao
            .AsNoTracking()
            .SingleOrDefaultAsync(
                registro => registro.Id == autorizacaoId && registro.PedidoId == pedidoId,
                cancellationToken)
            ?? throw ExcecaoAplicacao.NaoEncontrado(
                "authorization_not_found",
                "Autorização não encontrada para este pedido.");

        var agora = _relogio.UtcNow;

        _db.EventosAtendimento.Add(new EventoAtendimento
        {
            AtendimentoId = pedido.AtendimentoId,
            Tipo = TiposEventoAtendimento.JustificativaAdministrativaLida,
            AtorOpaco = ator.Id.ToString(),
            OcorridoEmUtc = agora,
            Correlacao = correlacao
        });

        await _auditoria.RegistrarAsync(
            new EventoAuditoriaNegocio(
                TiposAuditoriaNegocio.JustificativaLida,
                ator.Id,
                "autorizacao-retirada",
                concessao.Id,
                agora,
                correlacao,
                null),
            cancellationToken);

        var proposito = concessao.Tipo == TipoAutorizacaoRetirada.Documental
            ? PropositosCriptografia.JustificativaAutorizacaoDocumental
            : PropositosCriptografia.JustificativaAutorizacaoFinanceira;

        return new LeituraSensivel(
            _criptografia.DecifrarComRegistro(
                proposito,
                pedido.AtendimentoId,
                concessao.Id,
                concessao.JustificativaCifrada));
    }

    private async Task BloquearMutacaoSeEntregueAsync(
        PedidoRetirada pedido,
        Atendimento atendimento,
        CancellationToken cancellationToken)
    {
        var entregue =
            string.Equals(pedido.Estado, EstadoAtendimento.Entregue, StringComparison.Ordinal) ||
            string.Equals(atendimento.Estado, EstadoAtendimento.Entregue, StringComparison.Ordinal) ||
            await _db.EntregasVeiculo
                .AsNoTracking()
                .AnyAsync(entrega => entrega.PedidoId == pedido.Id, cancellationToken);

        if (entregue)
        {
            throw ExcecaoAplicacao.Conflito(
                CodigosBloqueioEntrega.JaConcluida,
                "O pedido já foi entregue e não admite nova concessão ou revogação de autorização.");
        }
    }

    private async Task<ResultadoAutorizacao?> BuscarRepeticaoAsync(
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
        return ApoioIdempotenciaFase3.Ler<ResultadoAutorizacao>(repeticao);
    }

    private async Task<ResultadoRevogacao?> BuscarRepeticaoRevogacaoAsync(
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
                    resultado.Operacao == ResultadoIdempotente.OperacaoRevogarAutorizacao &&
                    resultado.Chave == chaveIdempotente,
                cancellationToken);

        if (repeticao is null)
        {
            return null;
        }

        ApoioIdempotenciaFase3.VerificarHash(repeticao, hash);
        return ApoioIdempotenciaFase3.Ler<ResultadoRevogacao>(repeticao);
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

    private async Task<ResultadoAutorizacao?> ReservarAsync(
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
            return ApoioIdempotenciaFase3.Ler<ResultadoAutorizacao>(confirmado);
        }
    }

    private async Task<ResultadoRevogacao?> ReservarRevogacaoAsync(
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
            return ApoioIdempotenciaFase3.Ler<ResultadoRevogacao>(confirmado);
        }
    }
}
