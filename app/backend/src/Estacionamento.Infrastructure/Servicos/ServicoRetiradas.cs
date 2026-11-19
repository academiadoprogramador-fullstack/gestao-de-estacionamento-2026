using System.Data;
using Estacionamento.Application.Auditoria;
using Estacionamento.Application.Comum;
using Estacionamento.Application.Idempotencia;
using Estacionamento.Application.Retiradas;
using Estacionamento.Application.Tempo;
using Estacionamento.Domain.Atendimentos;
using Estacionamento.Domain.Idempotencia;
using Estacionamento.Domain.Retiradas;
using Estacionamento.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Estacionamento.Infrastructure.Servicos;

/// <summary>
/// Pedido de retirada e início de busca. Pedido, cobrança, razão inicial,
/// estado do atendimento, eventos, auditoria e resultado idempotente são
/// confirmados na mesma transação; um pedido por atendimento é garantido pelo
/// índice único e pelo bloqueio do atendimento.
/// </summary>
public sealed class ServicoRetiradas : IServicoRetiradas
{
    private readonly EstacionamentoDbContext _db;
    private readonly IAuditoriaNegocio _auditoria;
    private readonly IRelogio _relogio;
    private readonly IServicoConsultaRetiradas _consulta;

    public ServicoRetiradas(
        EstacionamentoDbContext db,
        IAuditoriaNegocio auditoria,
        IRelogio relogio,
        IServicoConsultaRetiradas consulta)
    {
        _db = db;
        _auditoria = auditoria;
        _relogio = relogio;
        _consulta = consulta;
    }

    public async Task<ResultadoPedidoRetirada> RegistrarPedidoAsync(
        AtorAtual ator,
        Guid atendimentoId,
        RequisicaoRegistrarPedidoRetirada requisicao,
        Guid chaveIdempotente,
        string? correlacao,
        CancellationToken cancellationToken = default)
    {
        if (!ator.EhOperador)
        {
            throw ExcecaoAplicacao.AcessoNegado();
        }

        ApoioIdempotenciaFase3.GarantirChave(chaveIdempotente);

        if (!requisicao.TitularPresente)
        {
            throw ExcecaoAplicacao.Validacao(
                "withdrawal_requires_holder",
                "Declare a presença do titular. Retirada por terceiro ou com titular ausente não é permitida no MVP.");
        }

        var conferencia = requisicao.Conferencia?.Trim() ?? string.Empty;
        if (!ConferenciaRetirada.EhValida(conferencia))
        {
            throw ExcecaoAplicacao.Validacao(
                "withdrawal_invalid_conference",
                "Informe a conferência aprovada ou divergente.");
        }

        if (atendimentoId == Guid.Empty)
        {
            throw ExcecaoAplicacao.Validacao(
                "withdrawal_invalid_attendance",
                "Informe o atendimento do pedido de retirada.");
        }

        var hash = CanonicalizadorIdempotencia.CalcularHashSolicitacaoRetiradaV1(
            ator.Id,
            atendimentoId,
            requisicao.TitularPresente,
            conferencia);

        await using var transacao = await _db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        var repeticao = await _db.ResultadosIdempotentes
            .AsNoTracking()
            .SingleOrDefaultAsync(
                resultado =>
                    resultado.AtorId == ator.Id &&
                    resultado.Operacao == ResultadoIdempotente.OperacaoSolicitarRetirada &&
                    resultado.Chave == chaveIdempotente,
                cancellationToken);

        if (repeticao is not null)
        {
            ApoioIdempotenciaFase3.VerificarHash(repeticao, hash);
            return ApoioIdempotenciaFase3.Ler<ResultadoPedidoRetirada>(repeticao);
        }

        var agora = _relogio.UtcNow;
        var reserva = new ResultadoIdempotente
        {
            Id = Guid.NewGuid(),
            AtorId = ator.Id,
            Operacao = ResultadoIdempotente.OperacaoSolicitarRetirada,
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

            var confirmado = await _db.ResultadosIdempotentes
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    resultado =>
                        resultado.AtorId == ator.Id &&
                        resultado.Operacao == ResultadoIdempotente.OperacaoSolicitarRetirada &&
                        resultado.Chave == chaveIdempotente,
                    cancellationToken);

            if (confirmado is null)
            {
                throw ExcecaoAplicacao.Conflito(
                    "idempotency_conflict",
                    "A mesma chave idempotente está em processamento com outro conteúdo.");
            }

            ApoioIdempotenciaFase3.VerificarHash(confirmado, hash);
            return ApoioIdempotenciaFase3.Ler<ResultadoPedidoRetirada>(confirmado);
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

            var pedidoExistente = await _db.PedidosRetirada
                .AsNoTracking()
                .AnyAsync(pedido => pedido.AtendimentoId == atendimentoId, cancellationToken);

            if (pedidoExistente)
            {
                throw ExcecaoAplicacao.Conflito(
                    "withdrawal_already_exists",
                    "Este atendimento já possui pedido de retirada. Um segundo pedido é recusado.");
            }

            if (!string.Equals(atendimento.Estado, EstadoAtendimento.Ativo, StringComparison.Ordinal))
            {
                throw ExcecaoAplicacao.Conflito(
                    "attendance_not_active",
                    "O atendimento não está ativo para receber um pedido de retirada.");
            }

            var calculo = CalculadoraCobranca.Calcular(
                atendimento.EntradaUtc,
                agora,
                atendimento.TarifaValorSnapshot);

            var pedido = new PedidoRetirada
            {
                Id = Guid.NewGuid(),
                AtendimentoId = atendimento.Id,
                Conferencia = conferencia,
                TitularPresente = true,
                Estado = EstadoAtendimento.Aguardando,
                SolicitadoEmUtc = agora,
                SolicitadoPorId = ator.Id
            };

            var cobranca = new Cobranca
            {
                Id = Guid.NewGuid(),
                PedidoId = pedido.Id,
                DuracaoTicks = calculo.DuracaoTicks,
                Diarias = calculo.Diarias,
                TarifaVersaoId = atendimento.TarifaVersaoId,
                TarifaValorSnapshot = atendimento.TarifaValorSnapshot,
                TotalOriginal = calculo.TotalOriginal,
                CriadaEmUtc = agora
            };

            var razao = new RazaoAtendimento
            {
                Id = Guid.NewGuid(),
                AtendimentoId = atendimento.Id,
                Revisao = 1
            };

            atendimento.Estado = EstadoAtendimento.Aguardando;

            var atorOpaco = ator.Id.ToString();
            _db.PedidosRetirada.Add(pedido);
            _db.Cobrancas.Add(cobranca);
            _db.RazoesAtendimento.Add(razao);
            _db.EventosAtendimento.AddRange(
                new EventoAtendimento
                {
                    AtendimentoId = atendimento.Id,
                    Tipo = TiposEventoAtendimento.PedidoRetiradaRegistrado,
                    AtorOpaco = atorOpaco,
                    OcorridoEmUtc = agora,
                    Correlacao = correlacao
                },
                new EventoAtendimento
                {
                    AtendimentoId = atendimento.Id,
                    Tipo = TiposEventoAtendimento.ConferenciaRetiradaRegistrada,
                    AtorOpaco = atorOpaco,
                    OcorridoEmUtc = agora,
                    Correlacao = correlacao
                },
                new EventoAtendimento
                {
                    AtendimentoId = atendimento.Id,
                    Tipo = TiposEventoAtendimento.CalculoCobrancaFixado,
                    AtorOpaco = atorOpaco,
                    OcorridoEmUtc = agora,
                    Correlacao = correlacao
                });

            _auditoria.Adicionar(new EventoAuditoriaNegocio(
                TiposAuditoriaNegocio.PedidoRetiradaRegistrado,
                ator.Id,
                "pedido-retirada",
                pedido.Id,
                agora,
                correlacao,
                null));

            var resultado = new ResultadoPedidoRetirada(
                pedido.Id,
                atendimento.Id,
                atendimento.IdentificadorPublico ?? string.Empty,
                pedido.Estado,
                pedido.SolicitadoEmUtc,
                cobranca.Diarias,
                cobranca.TotalOriginal,
                $"/api/v1/withdrawals/{pedido.Id}");

            reserva.AtendimentoId = atendimento.Id;
            reserva.IdentificadorPublico = atendimento.IdentificadorPublico;
            reserva.Estado = pedido.Estado;
            reserva.Location = resultado.Location;
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

            var confirmado = await _db.ResultadosIdempotentes
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    resultado =>
                        resultado.AtorId == ator.Id &&
                        resultado.Operacao == ResultadoIdempotente.OperacaoSolicitarRetirada &&
                        resultado.Chave == chaveIdempotente,
                    cancellationToken);

            if (confirmado?.ResultadoJson is not null)
            {
                ApoioIdempotenciaFase3.VerificarHash(confirmado, hash);
                return ApoioIdempotenciaFase3.Ler<ResultadoPedidoRetirada>(confirmado);
            }

            throw ExcecaoAplicacao.Conflito(
                "withdrawal_already_exists",
                "Este atendimento já possui pedido de retirada. Um segundo pedido é recusado.");
        }
    }

    public async Task<object> IniciarBuscaAsync(
        AtorAtual ator,
        Guid pedidoId,
        byte[] versaoEsperada,
        string? correlacao,
        CancellationToken cancellationToken = default)
    {
        if (versaoEsperada.Length == 0)
        {
            throw ExcecaoAplicacao.Validacao(
                "stale_withdrawal_version",
                "Informe a versão atual do pedido para iniciar a busca.");
        }

        await using var transacao = await _db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        var pedido = await _db.PedidosRetirada
            .FromSqlInterpolated(
                $"SELECT * FROM [PedidosRetirada] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {pedidoId}")
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw ExcecaoAplicacao.NaoEncontrado(
                "withdrawal_not_found",
                "Pedido de retirada não encontrado.");

        if (!pedido.RowVersion.SequenceEqual(versaoEsperada))
        {
            throw ExcecaoAplicacao.Conflito(
                "stale_withdrawal_version",
                "O pedido foi atualizado por outra operação. Recarregue o estado atual antes de tentar novamente.");
        }

        if (!string.Equals(pedido.Estado, EstadoAtendimento.Aguardando, StringComparison.Ordinal))
        {
            throw ExcecaoAplicacao.Conflito(
                "withdrawal_not_waiting",
                "O pedido não está mais aguardando busca. Recarregue a fila para ver o estado atual.");
        }

        var atendimento = await _db.Atendimentos
            .FromSqlInterpolated(
                $"SELECT * FROM [Atendimentos] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {pedido.AtendimentoId}")
            .SingleAsync(cancellationToken);

        var agora = _relogio.UtcNow;
        var atorOpaco = ator.Id.ToString();

        pedido.Estado = EstadoAtendimento.EmBusca;
        pedido.BuscaIniciadaEmUtc = agora;
        pedido.BuscaPorId = ator.Id;
        atendimento.Estado = EstadoAtendimento.EmBusca;

        _db.EventosAtendimento.Add(new EventoAtendimento
        {
            AtendimentoId = atendimento.Id,
            Tipo = TiposEventoAtendimento.BuscaIniciada,
            AtorOpaco = atorOpaco,
            OcorridoEmUtc = agora,
            Correlacao = correlacao
        });

        _auditoria.Adicionar(new EventoAuditoriaNegocio(
            TiposAuditoriaNegocio.BuscaIniciada,
            ator.Id,
            "pedido-retirada",
            pedido.Id,
            agora,
            correlacao,
            null));

        await _db.SaveChangesAsync(cancellationToken);
        await transacao.CommitAsync(cancellationToken);

        return await _consulta.ObterAsync(ator, pedido.Id, cancellationToken);
    }
}
