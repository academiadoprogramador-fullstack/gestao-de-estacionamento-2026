using System.Data;
using Estacionamento.Application.Auditoria;
using Estacionamento.Application.Atendimentos;
using Estacionamento.Application.Comum;
using Estacionamento.Application.Idempotencia;
using Estacionamento.Application.Seguranca;
using Estacionamento.Application.Tempo;
using Estacionamento.Domain.Atendimentos;
using Estacionamento.Domain.Idempotencia;
using Estacionamento.Domain.Tarifas;
using Estacionamento.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Estacionamento.Infrastructure.Servicos;

/// <summary>
/// Check-in transacional: reautoriza, normaliza, reserva a chave idempotente,
/// lê a tarifa sob bloqueio compartilhado, disputa a vaga, grava atendimento,
/// vistoria cifrada, ocupação, eventos, auditoria e resultado mínimo juntos.
/// </summary>
public sealed class ServicoAtendimentos : IServicoAtendimentos
{
    private readonly EstacionamentoDbContext _db;
    private readonly ICriptografiaCampos _criptografia;
    private readonly IAuditoriaNegocio _auditoria;
    private readonly IRelogio _relogio;

    public ServicoAtendimentos(
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

    public async Task<ResultadoEntrada> RegistrarEntradaAsync(
        AtorAtual ator,
        RequisicaoRegistrarEntrada requisicao,
        Guid chaveIdempotente,
        string? correlacao,
        CancellationToken cancellationToken = default)
    {
        if (!ator.EhOperador)
        {
            throw ExcecaoAplicacao.AcessoNegado();
        }

        if (chaveIdempotente == Guid.Empty)
        {
            throw ExcecaoAplicacao.Validacao(
                "idempotency_key_invalida",
                "O cabeçalho Idempotency-Key deve ser um UUID válido.");
        }

        var placa = NormalizadorPlaca.Normalizar(requisicao.Placa);
        var modelo = LimitesTextoAtendimento.Normalizar(requisicao.Modelo);
        var titular = LimitesTextoAtendimento.Normalizar(requisicao.Titular);
        var documento = LimitesTextoAtendimento.Normalizar(requisicao.Documento);
        var vistoria = LimitesTextoAtendimento.Normalizar(requisicao.Vistoria);
        var vagaId = requisicao.VagaId;

        var erros = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (!NormalizadorPlaca.EhValido(placa))
        {
            erros["placa"] = [$"A placa deve ter de 1 a {NormalizadorPlaca.TamanhoMaximo} caracteres alfanuméricos."];
        }

        if (!LimitesTextoAtendimento.EhValido(modelo, LimitesTextoAtendimento.Modelo))
        {
            erros["modelo"] = [$"O modelo é obrigatório e deve ter até {LimitesTextoAtendimento.Modelo} caracteres."];
        }

        if (!LimitesTextoAtendimento.EhValido(titular, LimitesTextoAtendimento.Titular))
        {
            erros["titular"] = [$"O titular é obrigatório e deve ter até {LimitesTextoAtendimento.Titular} caracteres."];
        }

        if (!LimitesTextoAtendimento.EhValido(documento, LimitesTextoAtendimento.Documento))
        {
            erros["documento"] = [$"O documento é obrigatório e deve ter até {LimitesTextoAtendimento.Documento} caracteres."];
        }

        if (!LimitesTextoAtendimento.EhValido(vistoria, LimitesTextoAtendimento.Vistoria))
        {
            erros["vistoria"] = [$"A vistoria é obrigatória e deve ter até {LimitesTextoAtendimento.Vistoria} caracteres."];
        }

        if (vagaId == Guid.Empty)
        {
            erros["vagaId"] = ["Selecione uma vaga livre."];
        }

        if (erros.Count > 0)
        {
            throw new ExcecaoAplicacao(
                TipoFalha.Validacao,
                "entrada_invalida",
                "A entrada contém dados inválidos.",
                erros);
        }

        var hash = CanonicalizadorIdempotencia.CalcularHashEntradaV1(
            ator.Id,
            vagaId,
            placa,
            modelo,
            titular,
            documento,
            vistoria);

        await using var transacao = await _db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        var repeticao = await _db.ResultadosIdempotentes
            .AsNoTracking()
            .SingleOrDefaultAsync(
                resultado =>
                    resultado.AtorId == ator.Id &&
                    resultado.Operacao == ResultadoIdempotente.OperacaoRegistrarEntrada &&
                    resultado.Chave == chaveIdempotente,
                cancellationToken);

        if (repeticao is not null)
        {
            return await ResolverReplayAsync(repeticao, hash, cancellationToken);
        }

        var agora = _relogio.UtcNow;
        var reserva = new ResultadoIdempotente
        {
            Id = Guid.NewGuid(),
            AtorId = ator.Id,
            Operacao = ResultadoIdempotente.OperacaoRegistrarEntrada,
            Chave = chaveIdempotente,
            VersaoCanonicalizacao = CanonicalizadorIdempotencia.VersaoAtual,
            HashSha256 = hash,
            CriadoEmUtc = agora
        };

        _db.ResultadosIdempotentes.Add(reserva);

        try
        {
            // A chave é reservada antes de reavaliar tarifa e vaga, na mesma
            // transação do comando.
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
                        resultado.Operacao == ResultadoIdempotente.OperacaoRegistrarEntrada &&
                        resultado.Chave == chaveIdempotente,
                    cancellationToken);

            if (confirmado is null)
            {
                throw ExcecaoAplicacao.Conflito(
                    "idempotency_conflict",
                    "A mesma chave idempotente está em processamento com outro conteúdo.");
            }

            return await ResolverReplayAsync(confirmado, hash, cancellationToken);
        }

        try
        {
            var configuracao = await _db.ConfiguracaoTarifa
                .FromSqlInterpolated(
                    $"SELECT * FROM [ConfiguracaoTarifa] WITH (HOLDLOCK) WHERE [Id] = {ConfiguracaoTarifa.IdSingleton}")
                .SingleOrDefaultAsync(cancellationToken);

            if (configuracao?.VersaoCorrenteId is null)
            {
                throw ExcecaoAplicacao.RegraNaoSatisfeita(
                    "rate_not_published",
                    "Nenhuma tarifa foi publicada. Publique uma tarifa antes de registrar a entrada.");
            }

            var tarifa = await _db.TarifasDiarias
                .AsNoTracking()
                .SingleAsync(registro => registro.Id == configuracao.VersaoCorrenteId, cancellationToken);

            var vaga = await _db.Vagas
                .FromSqlInterpolated(
                    $"SELECT * FROM [Vagas] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {vagaId}")
                .SingleOrDefaultAsync(cancellationToken);

            if (vaga is null)
            {
                throw ExcecaoAplicacao.Conflito(
                    "space_unavailable",
                    "A vaga informada não existe. Selecione uma vaga livre.");
            }

            var ocupada = await _db.Ocupacoes.AnyAsync(
                ocupacao => ocupacao.VagaId == vagaId && ocupacao.FimUtc == null,
                cancellationToken);

            if (ocupada)
            {
                throw ExcecaoAplicacao.Conflito(
                    "space_unavailable",
                    "A vaga selecionada já está ocupada. Selecione outra vaga livre.");
            }

            var sequencia = await ProximaSequenciaAsync(cancellationToken);

            var atendimentoId = Guid.NewGuid();
            var atendimento = new Atendimento
            {
                Id = atendimentoId,
                IdentificadorPublico = IdentificadorAtendimento.Formatar(agora.UtcDateTime.Year, sequencia),
                Placa = placa,
                Modelo = modelo,
                Titular = titular,
                DocumentoTitularCifrado = _criptografia.Cifrar(
                    PropositosCriptografia.Documento,
                    atendimentoId,
                    documento),
                TarifaVersaoId = tarifa.Id,
                TarifaValorSnapshot = tarifa.Valor,
                EntradaUtc = agora,
                OperadorId = ator.Id,
                Estado = EstadoAtendimento.Ativo
            };

            var vistoriaEntrada = new VistoriaEntrada
            {
                AtendimentoId = atendimento.Id,
                TextoCifrado = _criptografia.Cifrar(
                    PropositosCriptografia.VistoriaEntrada,
                    atendimento.Id,
                    vistoria),
                AtorId = ator.Id,
                CriadoEmUtc = agora
            };

            var ocupacao = new Ocupacao
            {
                Id = Guid.NewGuid(),
                VagaId = vagaId,
                AtendimentoId = atendimento.Id,
                InicioUtc = agora,
                ResponsavelId = ator.Id
            };

            var atorOpaco = ator.Id.ToString();
            _db.Atendimentos.Add(atendimento);
            _db.VistoriasEntrada.Add(vistoriaEntrada);
            _db.Ocupacoes.Add(ocupacao);
            _db.EventosAtendimento.AddRange(
                new EventoAtendimento
                {
                    AtendimentoId = atendimento.Id,
                    Tipo = TiposEventoAtendimento.EntradaRegistrada,
                    AtorOpaco = atorOpaco,
                    OcorridoEmUtc = agora,
                    Correlacao = correlacao
                },
                new EventoAtendimento
                {
                    AtendimentoId = atendimento.Id,
                    Tipo = TiposEventoAtendimento.VagaOcupada,
                    AtorOpaco = atorOpaco,
                    OcorridoEmUtc = agora,
                    Correlacao = correlacao,
                    DetalheOpaco = vagaId.ToString()
                });

            reserva.IdentificadorPublico = atendimento.IdentificadorPublico;
            reserva.AtendimentoId = atendimento.Id;
            reserva.VagaId = vagaId;
            reserva.Estado = atendimento.Estado;
            reserva.EntradaUtc = agora;
            reserva.Location = $"/api/v1/attendances/{atendimento.Id}";

            _auditoria.Adicionar(new EventoAuditoriaNegocio(
                TiposAuditoriaNegocio.EntradaRegistrada,
                ator.Id,
                "atendimento",
                atendimento.Id,
                agora,
                correlacao,
                null));

            await _db.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);

            return new ResultadoEntrada(
                atendimento.Id,
                atendimento.IdentificadorPublico,
                vagaId,
                vaga.Codigo,
                atendimento.Estado,
                atendimento.EntradaUtc,
                reserva.Location);
        }
        catch (DbUpdateException excecao) when (ErrosSql.ViolacaoUnicidadeEm(excecao, "Ocupacoes"))
        {
            await transacao.RollbackAsync(cancellationToken);
            throw ExcecaoAplicacao.Conflito(
                "space_unavailable",
                "A vaga selecionada já está ocupada. Selecione outra vaga livre.");
        }
        catch (DbUpdateException excecao) when (ErrosSql.EhViolacaoChaveEstrangeira(excecao))
        {
            await transacao.RollbackAsync(cancellationToken);
            throw ExcecaoAplicacao.Conflito(
                "space_unavailable",
                "A vaga selecionada não está mais disponível.");
        }
        catch (DbUpdateException excecao) when (ErrosSql.EhViolacaoUnicidade(excecao))
        {
            await transacao.RollbackAsync(cancellationToken);
            throw ExcecaoAplicacao.Conflito(
                "idempotency_conflict",
                "A chave idempotente foi reutilizada com outro conteúdo.");
        }
    }

    private async Task<long> ProximaSequenciaAsync(CancellationToken cancellationToken)
    {
        // `NEXT VALUE FOR` não pode ser usada em subconsulta/derived table, que
        // é como SqlQueryRaw envolveria o comando. A execução direta mantém o
        // valor gerado pelo SQL Server na mesma conexão/transação.
        var conexao = _db.Database.GetDbConnection();
        if (conexao.State != System.Data.ConnectionState.Open)
        {
            await conexao.OpenAsync(cancellationToken);
        }

        await using var comando = conexao.CreateCommand();
        comando.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
        comando.CommandText = "SELECT NEXT VALUE FOR [SequenciaIdentificadorAtendimento]";
        var resultado = await comando.ExecuteScalarAsync(cancellationToken);

        return Convert.ToInt64(resultado, System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task<ResultadoEntrada> ResolverReplayAsync(
        ResultadoIdempotente repeticao,
        byte[] hashAtual,
        CancellationToken cancellationToken)
    {
        if (!repeticao.HashSha256.SequenceEqual(hashAtual))
        {
            throw ExcecaoAplicacao.Conflito(
                "idempotency_conflict",
                "A mesma chave idempotente foi usada com conteúdo diferente.");
        }

        if (repeticao.AtendimentoId is null ||
            repeticao.VagaId is null ||
            repeticao.IdentificadorPublico is null ||
            repeticao.Estado is null ||
            repeticao.EntradaUtc is null ||
            repeticao.Location is null)
        {
            throw ExcecaoAplicacao.Conflito(
                "idempotency_conflict",
                "A tentativa anterior ainda não possui resultado confirmado.");
        }

        var codigoVaga = await _db.Vagas
            .AsNoTracking()
            .Where(vaga => vaga.Id == repeticao.VagaId)
            .Select(vaga => vaga.Codigo)
            .SingleAsync(cancellationToken);

        return new ResultadoEntrada(
            repeticao.AtendimentoId.Value,
            repeticao.IdentificadorPublico,
            repeticao.VagaId.Value,
            codigoVaga,
            repeticao.Estado,
            repeticao.EntradaUtc.Value,
            repeticao.Location);
    }
}
