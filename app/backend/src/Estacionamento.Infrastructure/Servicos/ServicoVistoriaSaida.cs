using System.Data;
using Estacionamento.Application.Auditoria;
using Estacionamento.Application.Comum;
using Estacionamento.Application.Entrega;
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
/// Registro imutável e leitura explícita da vistoria de saída. O comando
/// reautoriza os três perfis, reserva a chave idempotente, bloqueia o
/// atendimento, valida a versão e a inexistência de entrega/vistoria e grava
/// texto cifrado, evento, auditoria e recibo na mesma transação.
/// </summary>
public sealed class ServicoVistoriaSaida : IServicoVistoriaSaida
{
    private readonly EstacionamentoDbContext _db;
    private readonly ICriptografiaCampos _criptografia;
    private readonly IAuditoriaNegocio _auditoria;
    private readonly IRelogio _relogio;

    public ServicoVistoriaSaida(
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

    public async Task<ResultadoVistoriaSaida> RegistrarAsync(
        AtorAtual ator,
        Guid atendimentoId,
        RequisicaoVistoriaSaida requisicao,
        Guid chaveIdempotente,
        string? correlacao,
        CancellationToken cancellationToken = default)
    {
        if (!ator.TemPerfil(
                PerfisFixos.Administrador,
                PerfisFixos.Caixa,
                PerfisFixos.Manobrista))
        {
            throw ExcecaoAplicacao.AcessoNegado();
        }

        ApoioIdempotenciaFase3.GarantirChave(chaveIdempotente);

        var texto = LimitesTextoAtendimento.Normalizar(requisicao.Texto);
        if (!LimitesTextoAtendimento.EhValido(texto, LimitesTextoAtendimento.Vistoria))
        {
            throw ExcecaoAplicacao.Validacao(
                "exit_inspection_invalid_text",
                $"A vistoria de saída é obrigatória e deve ter de 1 a {LimitesTextoAtendimento.Vistoria} caracteres.");
        }

        if (atendimentoId == Guid.Empty)
        {
            throw ExcecaoAplicacao.Validacao(
                "exit_inspection_invalid_attendance",
                "Informe o atendimento da vistoria de saída.");
        }

        var versao = requisicao.VersaoAtendimentoEsperada ?? [];
        if (versao.Length == 0)
        {
            throw ExcecaoAplicacao.Validacao(
                "stale_attendance_version",
                "Informe a versão atual do atendimento para registrar a vistoria.");
        }

        var hash = CanonicalizadorIdempotencia.CalcularHashVistoriaSaidaV1(
            ator.Id,
            atendimentoId,
            texto,
            versao);

        await using var transacao = await _db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        var repeticao = await BuscarRepeticaoAsync(
            ator,
            chaveIdempotente,
            hash,
            cancellationToken);

        if (repeticao is not null)
        {
            return repeticao;
        }

        var agora = _relogio.UtcNow;
        var reserva = new ResultadoIdempotente
        {
            Id = Guid.NewGuid(),
            AtorId = ator.Id,
            Operacao = ResultadoIdempotente.OperacaoVistoriaSaida,
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

            return await BuscarRepeticaoAsync(ator, chaveIdempotente, hash, cancellationToken)
                ?? throw ExcecaoAplicacao.Conflito(
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

            if (!atendimento.RowVersion.SequenceEqual(versao))
            {
                throw ExcecaoAplicacao.Conflito(
                    "stale_attendance_version",
                    "O atendimento foi atualizado por outra operação. Recarregue o estado atual.");
            }

            var pedido = await _db.PedidosRetirada
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    registro => registro.AtendimentoId == atendimentoId,
                    cancellationToken)
                ?? throw ExcecaoAplicacao.RegraNaoSatisfeita(
                    "exit_inspection_requires_withdrawal",
                    "Registre o pedido de retirada antes da vistoria de saída.");

            var entregaExistente = await _db.EntregasVeiculo
                .AsNoTracking()
                .AnyAsync(entrega => entrega.AtendimentoId == atendimentoId, cancellationToken);

            if (entregaExistente ||
                string.Equals(atendimento.Estado, EstadoAtendimento.Entregue, StringComparison.Ordinal) ||
                string.Equals(pedido.Estado, EstadoAtendimento.Entregue, StringComparison.Ordinal))
            {
                throw ExcecaoAplicacao.Conflito(
                    CodigosBloqueioEntrega.JaConcluida,
                    "O atendimento já foi entregue e não admite nova vistoria de saída.");
            }

            var jaExiste = await _db.VistoriasSaida
                .AsNoTracking()
                .AnyAsync(vistoria => vistoria.AtendimentoId == atendimentoId, cancellationToken);

            if (jaExiste)
            {
                throw ExcecaoAplicacao.Conflito(
                    "exit_inspection_already_exists",
                    "Este atendimento já possui vistoria de saída. O registro é imutável.");
            }

            var vistoriaId = Guid.NewGuid();
            var vistoria = new VistoriaSaida
            {
                Id = vistoriaId,
                AtendimentoId = atendimento.Id,
                PedidoId = pedido.Id,
                TextoCifrado = _criptografia.CifrarComRegistro(
                    PropositosCriptografia.VistoriaSaida,
                    atendimento.Id,
                    vistoriaId,
                    texto),
                AtorId = ator.Id,
                CriadoEmUtc = agora
            };

            _db.VistoriasSaida.Add(vistoria);
            _db.EventosAtendimento.Add(new EventoAtendimento
            {
                AtendimentoId = atendimento.Id,
                Tipo = TiposEventoAtendimento.VistoriaSaidaRegistrada,
                AtorOpaco = ator.Id.ToString(),
                OcorridoEmUtc = agora,
                Correlacao = correlacao
            });

            _auditoria.Adicionar(new EventoAuditoriaNegocio(
                TiposAuditoriaNegocio.VistoriaSaidaRegistrada,
                ator.Id,
                "vistoria-saida",
                vistoriaId,
                agora,
                correlacao,
                null));

            var nomeAtor = await NomeDoAtorAsync(ator.Id, cancellationToken);
            var resultado = new ResultadoVistoriaSaida(
                vistoriaId,
                atendimento.Id,
                pedido.Id,
                nomeAtor,
                agora,
                $"/api/v1/attendances/{atendimento.Id}/exit-inspection");

            reserva.AtendimentoId = atendimento.Id;
            reserva.IdentificadorPublico = atendimento.IdentificadorPublico;
            reserva.Estado = atendimento.Estado;
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

            var confirmado = await BuscarRepeticaoAsync(
                ator,
                chaveIdempotente,
                hash,
                cancellationToken);

            if (confirmado is not null)
            {
                return confirmado;
            }

            throw ExcecaoAplicacao.Conflito(
                "exit_inspection_already_exists",
                "Este atendimento já possui vistoria de saída. O registro é imutável.");
        }
    }

    public async Task<LeituraVistoriaSaida> LerAsync(
        AtorAtual ator,
        Guid atendimentoId,
        string? correlacao,
        CancellationToken cancellationToken = default)
    {
        if (!ator.TemPerfil(
                PerfisFixos.Administrador,
                PerfisFixos.Caixa,
                PerfisFixos.Manobrista))
        {
            throw ExcecaoAplicacao.AcessoNegado();
        }

        var registro = await (
                from vistoria in _db.VistoriasSaida.AsNoTracking()
                join usuario in _db.Users.AsNoTracking()
                    on vistoria.AtorId equals usuario.Id
                where vistoria.AtendimentoId == atendimentoId
                select new
                {
                    vistoria.Id,
                    vistoria.TextoCifrado,
                    vistoria.CriadoEmUtc,
                    Ator = usuario.Nome
                })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw ExcecaoAplicacao.NaoEncontrado(
                "exit_inspection_not_found",
                "Vistoria de saída não encontrada.");

        // A auditoria é confirmada antes de qualquer decifragem. A falha
        // propaga e nenhum conteúdo é devolvido.
        var agora = _relogio.UtcNow;
        _db.EventosAtendimento.Add(new EventoAtendimento
        {
            AtendimentoId = atendimentoId,
            Tipo = TiposEventoAtendimento.VistoriaSaidaLida,
            AtorOpaco = ator.Id.ToString(),
            OcorridoEmUtc = agora,
            Correlacao = correlacao
        });

        await _auditoria.RegistrarAsync(
            new EventoAuditoriaNegocio(
                TiposAuditoriaNegocio.VistoriaSaidaLida,
                ator.Id,
                "vistoria-saida",
                registro.Id,
                agora,
                correlacao,
                null),
            cancellationToken);

        return new LeituraVistoriaSaida(
            _criptografia.DecifrarComRegistro(
                PropositosCriptografia.VistoriaSaida,
                atendimentoId,
                registro.Id,
                registro.TextoCifrado),
            registro.Ator,
            registro.CriadoEmUtc);
    }

    private async Task<ResultadoVistoriaSaida?> BuscarRepeticaoAsync(
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
                    resultado.Operacao == ResultadoIdempotente.OperacaoVistoriaSaida &&
                    resultado.Chave == chaveIdempotente,
                cancellationToken);

        if (repeticao is null)
        {
            return null;
        }

        ApoioIdempotenciaFase3.VerificarHash(repeticao, hash);
        return ApoioIdempotenciaFase3.Ler<ResultadoVistoriaSaida>(repeticao);
    }

    private Task<string> NomeDoAtorAsync(Guid atorId, CancellationToken cancellationToken) =>
        _db.Users
            .AsNoTracking()
            .Where(usuario => usuario.Id == atorId)
            .Select(usuario => usuario.Nome)
            .SingleAsync(cancellationToken);
}
