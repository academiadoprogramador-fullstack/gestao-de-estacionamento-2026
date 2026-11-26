using System.Data;
using System.Security.Cryptography;
using Estacionamento.Application.Auditoria;
using Estacionamento.Application.Comum;
using Estacionamento.Application.Configuracao;
using Estacionamento.Application.Fotos;
using Estacionamento.Application.Idempotencia;
using Estacionamento.Application.Seguranca;
using Estacionamento.Application.Tempo;
using Estacionamento.Domain.Atendimentos;
using Estacionamento.Domain.Fotos;
using Estacionamento.Domain.Idempotencia;
using Estacionamento.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Estacionamento.Infrastructure.Servicos;

/// <summary>
/// Envio, consulta e leitura auditada de fotos das vistorias. O binário é
/// processado fora da transação, a reserva é confirmada antes do R2 e os
/// metadados são confirmados em nova transação. Falhas externas deixam uma
/// tarefa durável de reconciliação/limpeza.
/// </summary>
public sealed class ServicoFotosVistoria : IServicoFotosVistoria
{
    private readonly EstacionamentoDbContext _db;
    private readonly IArmazenamentoFotos _armazenamento;
    private readonly IProcessadorImagens _processador;
    private readonly IAuditoriaNegocio _auditoria;
    private readonly IRelogio _relogio;
    private readonly IContextoAplicacao _contexto;
    private readonly ArmazenamentoFotosOptions _opcoes;
    private readonly ILogger<ServicoFotosVistoria> _logger;

    public ServicoFotosVistoria(
        EstacionamentoDbContext db,
        IArmazenamentoFotos armazenamento,
        IProcessadorImagens processador,
        IAuditoriaNegocio auditoria,
        IRelogio relogio,
        IContextoAplicacao contexto,
        IOptions<ArmazenamentoFotosOptions> opcoes,
        ILogger<ServicoFotosVistoria> logger)
    {
        _db = db;
        _armazenamento = armazenamento;
        _processador = processador;
        _auditoria = auditoria;
        _relogio = relogio;
        _contexto = contexto;
        _opcoes = opcoes.Value;
        _logger = logger;
    }

    public async Task<ResultadoFoto> EnviarAsync(
        AtorAtual ator,
        Guid atendimentoId,
        string tipo,
        ArquivoFoto arquivo,
        byte[] versaoAtendimentoEsperada,
        Guid chaveIdempotente,
        string? correlacao,
        CancellationToken cancellationToken = default)
    {
        ValidarComando(ator, atendimentoId, tipo, versaoAtendimentoEsperada, chaveIdempotente);

        if (!_opcoes.Habilitado)
        {
            throw ExcecaoAplicacao.Indisponivel(
                "photo_storage_disabled",
                "O envio de fotos está desabilitado. A vistoria textual continua disponível.");
        }

        var bytesEntrada = await LerComLimiteAsync(
            arquivo.Conteudo,
            (long)_opcoes.Limites.TamanhoMaximoArquivoMiB * 1024 * 1024,
            arquivo.TamanhoDeclarado,
            cancellationToken);
        var hashEntrada = SHA256.HashData(bytesEntrada);
        var operacao = Operacao(tipo);
        var hash = CanonicalizadorIdempotencia.CalcularHashFotoV1(
            ator.Id,
            atendimentoId,
            tipo,
            versaoAtendimentoEsperada,
            hashEntrada);

        var repeticao = await _db.ResultadosIdempotentes
            .AsNoTracking()
            .SingleOrDefaultAsync(
                resultado => resultado.AtorId == ator.Id &&
                             resultado.Operacao == operacao &&
                             resultado.Chave == chaveIdempotente,
                cancellationToken);

        FotoVistoria? foto;
        if (repeticao is not null)
        {
            if (!CryptographicOperations.FixedTimeEquals(repeticao.HashSha256, hash))
            {
                throw ExcecaoAplicacao.Conflito(
                    "photo_idempotency_conflict",
                    "A mesma chave idempotente foi reutilizada com outro conteúdo.");
            }

            if (!string.IsNullOrWhiteSpace(repeticao.ResultadoJson) && repeticao.CodigoResposta is not null)
            {
                var fotoConfirmada = await _db.FotosVistoria
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        registro => registro.ResultadoIdempotenteId == repeticao.Id,
                        cancellationToken);

                if (fotoConfirmada is null ||
                    !string.Equals(fotoConfirmada.Estado, EstadosFotoVistoria.Disponivel, StringComparison.Ordinal))
                {
                    throw ExcecaoAplicacao.NaoEncontrado(
                        "photo_expired",
                        "A foto venceu pela retenção e não está mais disponível.");
                }

                return ApoioFotos.DesserializarResultado(repeticao.ResultadoJson);
            }

            foto = await _db.FotosVistoria
                .SingleOrDefaultAsync(
                    registro => registro.ResultadoIdempotenteId == repeticao.Id,
                    cancellationToken)
                ?? throw ExcecaoAplicacao.Conflito(
                    "photo_idempotency_conflict",
                    "A tentativa de foto não está mais disponível para repetição.");

            if (foto.Estado == EstadosFotoVistoria.Disponivel)
            {
                return ApoioFotos.MontarResultado(foto);
            }

            if (!string.Equals(foto.Estado, EstadosFotoVistoria.Pendente, StringComparison.Ordinal) &&
                !string.Equals(foto.Estado, EstadosFotoVistoria.LimpezaPendente, StringComparison.Ordinal))
            {
                throw ExcecaoAplicacao.Conflito(
                    "photo_idempotency_conflict",
                    "A tentativa de foto já foi descartada e não pode ser repetida.");
            }
        }
        else
        {
            var atendimento = await CarregarAtendimentoSemBloqueioAsync(atendimentoId, cancellationToken);
            ValidarVersao(atendimento, versaoAtendimentoEsperada);
            await GarantirJanelaAsync(atendimento, tipo, cancellationToken);

            var imagem = await _processador.ProcessarAsync(
                new MemoryStream(bytesEntrada, writable: false),
                arquivo.TipoConteudo,
                cancellationToken);

            foto = await ReservarAsync(
                ator,
                atendimentoId,
                tipo,
                imagem,
                hashEntrada,
                versaoAtendimentoEsperada,
                operacao,
                hash,
                chaveIdempotente,
                cancellationToken);
        }

        try
        {
            if (!await _armazenamento.ExisteAsync(foto.ChaveObjeto, cancellationToken))
            {
                var imagem = await _processador.ProcessarAsync(
                    new MemoryStream(bytesEntrada, writable: false),
                    arquivo.TipoConteudo,
                    cancellationToken);

                await _armazenamento.EnviarAsync(
                    foto.ChaveObjeto,
                    new MemoryStream(imagem.Conteudo, writable: false),
                    imagem.Mime,
                    cancellationToken);
            }

            return await ConfirmarAsync(foto.Id, ator, correlacao, cancellationToken);
        }
        catch (ExcecaoAplicacao excecao) when (excecao.Tipo is TipoFalha.Indisponivel or TipoFalha.Conflito)
        {
            await MarcarFalhaAsync(foto.Id, excecao.Codigo, cancellationToken);
            throw;
        }
        catch
        {
            await MarcarFalhaAsync(foto.Id, "photo_storage_unavailable", cancellationToken);
            throw;
        }
    }

    public async Task<ListaFotosVistoria> ListarAsync(
        AtorAtual ator,
        Guid atendimentoId,
        string tipo,
        CancellationToken cancellationToken = default)
    {
        ValidarTipo(tipo);
        var atendimento = await CarregarAtendimentoSemBloqueioAsync(atendimentoId, cancellationToken);
        var vencido = await ApoioFotos.EstaVencidaAsync(
            _db,
            atendimentoId,
            _opcoes.RetencaoDias,
            _relogio.UtcNow,
            cancellationToken);

        var janela = await ObterJanelaAsync(atendimento, tipo, cancellationToken);
        var itens = vencido
            ? []
            : await _db.FotosVistoria
                .AsNoTracking()
                .Where(foto =>
                    foto.AtendimentoId == atendimentoId &&
                    foto.Tipo == tipo &&
                    foto.Estado == EstadosFotoVistoria.Disponivel)
                .OrderBy(foto => foto.CriadoEmUtc)
                .ThenBy(foto => foto.Id)
                .Select(foto => new FotoVistoriaResumo(
                    foto.Id,
                    foto.Tipo,
                    foto.Mime,
                    foto.TamanhoBytes,
                    foto.Largura,
                    foto.Altura,
                    foto.CriadoEmUtc))
                .ToListAsync(cancellationToken);

        return new ListaFotosVistoria(
            itens,
            itens.Count,
            janela,
            vencido,
            await ObterOrcamentoAsync(cancellationToken));
    }

    public async Task<ConteudoFoto> AbrirConteudoAsync(
        AtorAtual ator,
        Guid atendimentoId,
        string tipo,
        Guid fotoId,
        string? correlacao,
        CancellationToken cancellationToken = default)
    {
        ValidarTipo(tipo);

        if (!_opcoes.Habilitado)
        {
            throw ExcecaoAplicacao.Indisponivel(
                "photo_storage_disabled",
                "A leitura de fotos está desabilitada. A vistoria textual continua disponível.");
        }

        var foto = await _db.FotosVistoria
            .AsNoTracking()
            .SingleOrDefaultAsync(
                registro => registro.Id == fotoId &&
                            registro.AtendimentoId == atendimentoId &&
                            registro.Tipo == tipo &&
                            registro.Estado == EstadosFotoVistoria.Disponivel,
                cancellationToken)
            ?? throw ExcecaoAplicacao.NaoEncontrado(
                "photo_not_found",
                "Foto não encontrada para esta vistoria.");

        if (await ApoioFotos.EstaVencidaAsync(
                _db,
                atendimentoId,
                _opcoes.RetencaoDias,
                _relogio.UtcNow,
                cancellationToken))
        {
            throw ExcecaoAplicacao.NaoEncontrado(
                "photo_expired",
                "A foto venceu pela retenção e não está mais disponível.");
        }

        var agora = _relogio.UtcNow;
        _db.EventosAtendimento.Add(new EventoAtendimento
        {
            AtendimentoId = atendimentoId,
            Tipo = TiposEventoAtendimento.FotoLida,
            AtorOpaco = ator.Id.ToString(),
            OcorridoEmUtc = agora,
            Correlacao = correlacao
        });

        await _auditoria.RegistrarAsync(
            new EventoAuditoriaNegocio(
                TiposAuditoriaNegocio.FotoLida,
                ator.Id,
                "foto",
                foto.Id,
                agora,
                correlacao,
                null),
            cancellationToken);

        var conteudo = await _armazenamento.AbrirAsync(foto.ChaveObjeto, cancellationToken);
        return new ConteudoFoto(
            conteudo,
            foto.Mime,
            foto.TamanhoBytes,
            $"foto-{foto.Id:N}.webp");
    }

    public async Task<ResumoFotosVistoria> ObterResumoAsync(
        AtorAtual ator,
        Guid atendimentoId,
        CancellationToken cancellationToken = default)
    {
        var atendimento = await CarregarAtendimentoSemBloqueioAsync(atendimentoId, cancellationToken);
        var vencido = await ApoioFotos.EstaVencidaAsync(
            _db,
            atendimentoId,
            _opcoes.RetencaoDias,
            _relogio.UtcNow,
            cancellationToken);

        var entrada = vencido
            ? 0
            : await _db.FotosVistoria
                .AsNoTracking()
                .CountAsync(
                    foto => foto.AtendimentoId == atendimentoId &&
                            foto.Tipo == TiposVistoriaFoto.Entrada &&
                            foto.Estado == EstadosFotoVistoria.Disponivel,
                    cancellationToken);
        var saida = vencido
            ? 0
            : await _db.FotosVistoria
                .AsNoTracking()
                .CountAsync(
                    foto => foto.AtendimentoId == atendimentoId &&
                            foto.Tipo == TiposVistoriaFoto.Saida &&
                            foto.Estado == EstadosFotoVistoria.Disponivel,
                    cancellationToken);

        return new ResumoFotosVistoria(
            entrada,
            saida,
            vencido ? "encerrada" : await ObterJanelaAsync(atendimento, TiposVistoriaFoto.Entrada, cancellationToken),
            vencido ? "encerrada" : await ObterJanelaAsync(atendimento, TiposVistoriaFoto.Saida, cancellationToken),
            await ObterOrcamentoAsync(cancellationToken));
    }

    private async Task<FotoVistoria> ReservarAsync(
        AtorAtual ator,
        Guid atendimentoId,
        string tipo,
        ImagemProcessada imagem,
        byte[] hashEntrada,
        byte[] versaoAtendimento,
        string operacao,
        byte[] hash,
        Guid chaveIdempotente,
        CancellationToken cancellationToken)
    {
        for (var tentativa = 0; tentativa < 3; tentativa += 1)
        {
            try
            {
                _db.ChangeTracker.Clear();
                return await ReservarUmaVezAsync(
                    ator,
                    atendimentoId,
                    tipo,
                    imagem,
                    hashEntrada,
                    versaoAtendimento,
                    operacao,
                    hash,
                    chaveIdempotente,
                    cancellationToken);
            }
            catch (DbUpdateConcurrencyException) when (tentativa < 2)
            {
                await Task.Delay(20 * (tentativa + 1), cancellationToken);
            }
            catch (DbUpdateException excecao) when (
                tentativa < 2 && ErrosSql.EhViolacaoUnicidade(excecao))
            {
                _db.ChangeTracker.Clear();
                var existente = await _db.ResultadosIdempotentes
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        resultado => resultado.AtorId == ator.Id &&
                                     resultado.Operacao == operacao &&
                                     resultado.Chave == chaveIdempotente,
                        cancellationToken);

                if (existente is not null)
                {
                    var fotoExistente = await _db.FotosVistoria
                        .SingleOrDefaultAsync(
                            foto => foto.ResultadoIdempotenteId == existente.Id,
                            cancellationToken);

                    if (fotoExistente is not null)
                    {
                        return fotoExistente;
                    }
                }

                await Task.Delay(20 * (tentativa + 1), cancellationToken);
            }
        }

        throw ExcecaoAplicacao.Conflito(
            "concurrency_conflict",
            "Não foi possível reservar a foto sob concorrência. Recarregue o estado atual.");
    }

    private async Task<FotoVistoria> ReservarUmaVezAsync(
        AtorAtual ator,
        Guid atendimentoId,
        string tipo,
        ImagemProcessada imagem,
        byte[] hashEntrada,
        byte[] versaoAtendimento,
        string operacao,
        byte[] hash,
        Guid chaveIdempotente,
        CancellationToken cancellationToken)
    {
        await using var transacao = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var atendimento = await _db.Atendimentos
            .FromSqlInterpolated(
                $"SELECT * FROM [Atendimentos] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {atendimentoId}")
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw ExcecaoAplicacao.NaoEncontrado(
                "atendimento_nao_encontrado",
                "Atendimento não encontrado.");

        ValidarVersao(atendimento, versaoAtendimento);
        await GarantirJanelaAsync(atendimento, tipo, cancellationToken);

        var total = await _db.FotosVistoria.CountAsync(
            foto => foto.AtendimentoId == atendimentoId &&
                    foto.Tipo == tipo &&
                    (foto.Estado == EstadosFotoVistoria.Pendente ||
                     foto.Estado == EstadosFotoVistoria.Disponivel),
            cancellationToken);

        if (RegraFotosVistoria.LimiteAtingido(total))
        {
            throw ExcecaoAplicacao.Conflito(
                "photo_limit_reached",
                "O limite de cinco fotos por vistoria foi atingido.");
        }

        var consumo = await ObterOuCriarConsumoAsync(cancellationToken);
        var totalConsumo = await ApoioFotos.ObterConsumoTotalAsync(
            _db,
            ApoioFotos.Competencia(_relogio.UtcNow),
            cancellationToken);
        ApoioFotos.GarantirOrcamentoDisponivel(
            totalConsumo,
            _opcoes,
            imagem.Conteudo.LongLength,
            1,
            0);

        var agora = _relogio.UtcNow;
        var resultado = new ResultadoIdempotente
        {
            Id = Guid.NewGuid(),
            AtorId = ator.Id,
            Operacao = operacao,
            Chave = chaveIdempotente,
            VersaoCanonicalizacao = CanonicalizadorIdempotencia.VersaoAtual,
            HashSha256 = hash,
            CriadoEmUtc = agora,
            AtendimentoId = atendimentoId,
            Estado = EstadosFotoVistoria.Pendente
        };

        var foto = new FotoVistoria
        {
            Id = Guid.NewGuid(),
            AtendimentoId = atendimentoId,
            Tipo = tipo,
            VistoriaEntradaId = string.Equals(tipo, TiposVistoriaFoto.Entrada, StringComparison.Ordinal)
                ? atendimentoId
                : null,
            VistoriaSaidaId = null,
            ChaveObjeto = $"fotos/{atendimentoId:N}/{tipo}/{Guid.NewGuid():N}",
            Estado = EstadosFotoVistoria.Pendente,
            Mime = imagem.Mime,
            TamanhoBytes = imagem.Conteudo.LongLength,
            Largura = imagem.Largura,
            Altura = imagem.Altura,
            HashSha256 = imagem.HashSha256,
            HashEntradaSha256 = hashEntrada,
            AtorId = ator.Id,
            CriadoEmUtc = agora,
            ReservadoAteUtc = agora.AddHours(24),
            VersaoAtendimentoBase = versaoAtendimento,
            ResultadoIdempotenteId = resultado.Id
        };

        if (string.Equals(tipo, TiposVistoriaFoto.Saida, StringComparison.Ordinal))
        {
            foto.VistoriaEntradaId = null;
            foto.VistoriaSaidaId = await _db.VistoriasSaida
                .AsNoTracking()
                .Where(vistoria => vistoria.AtendimentoId == atendimentoId)
                .Select(vistoria => (Guid?)vistoria.Id)
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw ExcecaoAplicacao.RegraNaoSatisfeita(
                    "photo_requires_inspection",
                    "Registre a vistoria de saída antes de anexar fotos.");
        }

        _db.ResultadosIdempotentes.Add(resultado);
        _db.FotosVistoria.Add(foto);

        consumo.BytesReservados += imagem.Conteudo.LongLength;
        consumo.OperacoesClasseA += 1;
        consumo.AtualizadoEmUtc = agora;

        await _db.SaveChangesAsync(cancellationToken);
        await transacao.CommitAsync(cancellationToken);
        return foto;
    }

    private async Task<ResultadoFoto> ConfirmarAsync(
        Guid fotoId,
        AtorAtual ator,
        string? correlacao,
        CancellationToken cancellationToken)
    {
        for (var tentativa = 0; tentativa < 3; tentativa += 1)
        {
            try
            {
                _db.ChangeTracker.Clear();
                return await ConfirmarUmaVezAsync(fotoId, ator, correlacao, cancellationToken);
            }
            catch (DbUpdateConcurrencyException) when (tentativa < 2)
            {
                await Task.Delay(20 * (tentativa + 1), cancellationToken);
            }
            catch (DbUpdateException excecao) when (
                tentativa < 2 && ErrosSql.EhViolacaoUnicidade(excecao))
            {
                await Task.Delay(20 * (tentativa + 1), cancellationToken);
            }
        }

        throw ExcecaoAplicacao.Conflito(
            "concurrency_conflict",
            "Não foi possível confirmar a foto sob concorrência. Recarregue o estado atual.");
    }

    private async Task<ResultadoFoto> ConfirmarUmaVezAsync(
        Guid fotoId,
        AtorAtual ator,
        string? correlacao,
        CancellationToken cancellationToken)
    {
        await using var transacao = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var foto = await _db.FotosVistoria
            .SingleAsync(registro => registro.Id == fotoId, cancellationToken);

        if (string.Equals(foto.Estado, EstadosFotoVistoria.Disponivel, StringComparison.Ordinal))
        {
            await transacao.CommitAsync(cancellationToken);
            return ApoioFotos.MontarResultado(foto);
        }

        if (!string.Equals(foto.Estado, EstadosFotoVistoria.Pendente, StringComparison.Ordinal) &&
            !string.Equals(foto.Estado, EstadosFotoVistoria.LimpezaPendente, StringComparison.Ordinal))
        {
            throw ExcecaoAplicacao.Conflito(
                "photo_idempotency_conflict",
                "A tentativa de foto não está mais pendente.");
        }

        var atendimento = await _db.Atendimentos
            .FromSqlInterpolated(
                $"SELECT * FROM [Atendimentos] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {foto.AtendimentoId}")
            .SingleAsync(cancellationToken);

        ValidarVersao(atendimento, foto.VersaoAtendimentoBase);
        await GarantirJanelaAsync(atendimento, foto.Tipo, cancellationToken);

        var agora = _relogio.UtcNow;
        foto.Estado = EstadosFotoVistoria.Disponivel;
        foto.ConfirmadoEmUtc = agora;
        foto.ReservadoAteUtc = null;

        var tarefasLimpeza = await _db.TarefasFotos
            .Where(tarefa => tarefa.FotoId == foto.Id &&
                             tarefa.Tipo == TiposTarefaFoto.Limpeza &&
                             tarefa.Estado == "pendente")
            .ToListAsync(cancellationToken);

        foreach (var tarefa in tarefasLimpeza)
        {
            tarefa.Estado = "concluida";
            tarefa.LeaseAteUtc = null;
        }

        var resultado = await _db.ResultadosIdempotentes
            .SingleAsync(registro => registro.Id == foto.ResultadoIdempotenteId, cancellationToken);
        var recibo = ApoioFotos.MontarResultado(foto);
        resultado.ResultadoJson = ApoioFotos.SerializarResultado(recibo);
        resultado.CodigoResposta = 201;
        resultado.Estado = EstadosFotoVistoria.Disponivel;
        resultado.Location = recibo.Location;

        _db.EventosAtendimento.Add(new EventoAtendimento
        {
            AtendimentoId = foto.AtendimentoId,
            Tipo = TiposEventoAtendimento.FotoAnexada,
            AtorOpaco = ator.Id.ToString(),
            OcorridoEmUtc = agora,
            Correlacao = correlacao
        });

        _auditoria.Adicionar(new EventoAuditoriaNegocio(
            TiposAuditoriaNegocio.FotoAnexada,
            ator.Id,
            "foto",
            foto.Id,
            agora,
            correlacao,
            foto.Tipo));

        var consumo = await ObterOuCriarConsumoAsync(cancellationToken);
        consumo.BytesReservados = Math.Max(0, consumo.BytesReservados - foto.TamanhoBytes);
        consumo.BytesArmazenados += foto.TamanhoBytes;
        consumo.AtualizadoEmUtc = agora;

        await _db.SaveChangesAsync(cancellationToken);
        await transacao.CommitAsync(cancellationToken);
        return recibo;
    }

    private async Task MarcarFalhaAsync(Guid fotoId, string codigo, CancellationToken cancellationToken)
    {
        try
        {
            _db.ChangeTracker.Clear();
            var foto = await _db.FotosVistoria
                .SingleOrDefaultAsync(registro => registro.Id == fotoId, cancellationToken);

            if (foto is null)
            {
                return;
            }

            if (string.Equals(foto.Estado, EstadosFotoVistoria.Pendente, StringComparison.Ordinal) ||
                string.Equals(foto.Estado, EstadosFotoVistoria.Disponivel, StringComparison.Ordinal))
            {
                foto.Estado = EstadosFotoVistoria.LimpezaPendente;
                foto.ReservadoAteUtc = null;
            }

            if (!await _db.TarefasFotos.AnyAsync(
                    tarefa => tarefa.FotoId == fotoId &&
                              tarefa.Tipo == TiposTarefaFoto.Limpeza &&
                              tarefa.Estado == "pendente",
                    cancellationToken))
            {
                _db.TarefasFotos.Add(new TarefaFoto
                {
                    Id = Guid.NewGuid(),
                    FotoId = fotoId,
                    Tipo = TiposTarefaFoto.Limpeza,
                    Estado = "pendente",
                    CriadoEmUtc = _relogio.UtcNow,
                    UltimoErroCodigo = codigo
                });
            }

            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception excecao)
        {
            _logger.LogWarning(
                "Não foi possível registrar falha de foto ({Codigo}). Tipo: {Tipo}.",
                codigo,
                excecao.GetType().Name);
        }
    }

    private async Task<ConsumoFotosMensal> ObterOuCriarConsumoAsync(CancellationToken cancellationToken)
    {
        var competencia = ApoioFotos.Competencia(_relogio.UtcNow);
        var ambiente = _contexto.NomeAmbiente;

        var consumo = await _db.ConsumosFotosMensais
            .FromSqlInterpolated(
                $"SELECT * FROM [ConsumosFotosMensais] WITH (UPDLOCK, HOLDLOCK) WHERE [Competencia] = {competencia} AND [Ambiente] = {ambiente}")
            .SingleOrDefaultAsync(cancellationToken);

        if (consumo is not null)
        {
            return consumo;
        }

        consumo = new ConsumoFotosMensal
        {
            Id = Guid.NewGuid(),
            Competencia = competencia,
            Ambiente = ambiente,
            AtualizadoEmUtc = _relogio.UtcNow
        };

        _db.ConsumosFotosMensais.Add(consumo);
        await _db.SaveChangesAsync(cancellationToken);
        return consumo;
    }

    private async Task<OrcamentoFotosResumo> ObterOrcamentoAsync(CancellationToken cancellationToken)
    {
        if (!_opcoes.Habilitado)
        {
            return new OrcamentoFotosResumo(
                0,
                0,
                0,
                false,
                true,
                "desabilitado",
                _opcoes.Limites.MaximoFotosPorVistoria);
        }

        var consumo = await ApoioFotos.ObterConsumoTotalAsync(
            _db,
            ApoioFotos.Competencia(_relogio.UtcNow),
            cancellationToken);
        return ApoioFotos.Resumir(consumo, _opcoes);
    }

    private async Task<string> ObterJanelaAsync(
        Atendimento atendimento,
        string tipo,
        CancellationToken cancellationToken)
    {
        var entregue = string.Equals(atendimento.Estado, EstadoAtendimento.Entregue, StringComparison.Ordinal);
        var temPedido = await _db.PedidosRetirada
            .AsNoTracking()
            .AnyAsync(pedido => pedido.AtendimentoId == atendimento.Id, cancellationToken);
        var vistoriaEntrada = await _db.VistoriasEntrada
            .AsNoTracking()
            .AnyAsync(vistoria => vistoria.AtendimentoId == atendimento.Id, cancellationToken);
        var vistoriaSaida = await _db.VistoriasSaida
            .AsNoTracking()
            .AnyAsync(vistoria => vistoria.AtendimentoId == atendimento.Id, cancellationToken);

        var aberta = string.Equals(tipo, TiposVistoriaFoto.Entrada, StringComparison.Ordinal)
            ? vistoriaEntrada && RegraFotosVistoria.EntradaAberta(temPedido, entregue)
            : RegraFotosVistoria.SaidaAberta(vistoriaSaida, entregue);

        return aberta ? "aberta" : "encerrada";
    }

    private async Task GarantirJanelaAsync(
        Atendimento atendimento,
        string tipo,
        CancellationToken cancellationToken)
    {
        if (string.Equals(tipo, TiposVistoriaFoto.Entrada, StringComparison.Ordinal))
        {
            var vistoriaEntrada = await _db.VistoriasEntrada
                .AsNoTracking()
                .AnyAsync(vistoria => vistoria.AtendimentoId == atendimento.Id, cancellationToken);

            if (!vistoriaEntrada)
            {
                throw ExcecaoAplicacao.RegraNaoSatisfeita(
                    "photo_requires_inspection",
                    "Registre a vistoria de entrada antes de anexar fotos.");
            }
        }
        else
        {
            var vistoriaSaida = await _db.VistoriasSaida
                .AsNoTracking()
                .AnyAsync(vistoria => vistoria.AtendimentoId == atendimento.Id, cancellationToken);

            if (!vistoriaSaida)
            {
                throw ExcecaoAplicacao.RegraNaoSatisfeita(
                    "photo_requires_inspection",
                    "Registre a vistoria de saída antes de anexar fotos.");
            }
        }

        var janela = await ObterJanelaAsync(atendimento, tipo, cancellationToken);
        if (!string.Equals(janela, "aberta", StringComparison.Ordinal))
        {
            throw ExcecaoAplicacao.Conflito(
                "photo_window_closed",
                string.Equals(tipo, TiposVistoriaFoto.Entrada, StringComparison.Ordinal)
                    ? "A janela de fotos de entrada encerrou com o pedido de retirada."
                    : "A janela de fotos de saída encerrou com a entrega.");
        }
    }

    private async Task<Atendimento> CarregarAtendimentoSemBloqueioAsync(
        Guid atendimentoId,
        CancellationToken cancellationToken) =>
        await _db.Atendimentos
            .AsNoTracking()
            .SingleOrDefaultAsync(atendimento => atendimento.Id == atendimentoId, cancellationToken)
        ?? throw ExcecaoAplicacao.NaoEncontrado(
            "atendimento_nao_encontrado",
            "Atendimento não encontrado.");

    private static void ValidarVersao(Atendimento atendimento, byte[] versao)
    {
        if (!atendimento.RowVersion.SequenceEqual(versao))
        {
            throw ExcecaoAplicacao.Conflito(
                "stale_attendance_version",
                "O atendimento foi atualizado por outra operação. Recarregue o estado atual.");
        }
    }

    private static void ValidarComando(
        AtorAtual ator,
        Guid atendimentoId,
        string tipo,
        byte[] versao,
        Guid chave)
    {
        ValidarTipo(tipo);

        if (!RegraFotosVistoria.PerfilPodeEnviar(tipo, ator.EhOperador))
        {
            throw ExcecaoAplicacao.AcessoNegado();
        }

        if (atendimentoId == Guid.Empty || versao.Length == 0)
        {
            throw ExcecaoAplicacao.Validacao(
                "photo_invalid_request",
                "Informe o atendimento e a versão atual para anexar a foto.");
        }

        if (chave == Guid.Empty)
        {
            throw ExcecaoAplicacao.Validacao(
                "idempotency_key_required",
                "Informe a Idempotency-Key em formato UUID.");
        }
    }

    private static void ValidarTipo(string tipo)
    {
        if (!TiposVistoriaFoto.EhValido(tipo))
        {
            throw ExcecaoAplicacao.Validacao(
                "photo_invalid_type",
                "O tipo da vistoria deve ser entrada ou saida.");
        }
    }

    private static string Operacao(string tipo) =>
        string.Equals(tipo, TiposVistoriaFoto.Entrada, StringComparison.Ordinal)
            ? ResultadoIdempotente.OperacaoFotoEntrada
            : ResultadoIdempotente.OperacaoFotoSaida;

    private static async Task<byte[]> LerComLimiteAsync(
        Stream conteudo,
        long limite,
        long tamanhoDeclarado,
        CancellationToken cancellationToken)
    {
        if (tamanhoDeclarado > limite)
        {
            throw ExcecaoAplicacao.ArquivoMuitoGrande(
                "photo_too_large",
                "A foto excede o limite de 5 MiB por arquivo.");
        }

        using var memoria = new MemoryStream();
        var buffer = new byte[81920];
        long total = 0;

        while (true)
        {
            var lidos = await conteudo.ReadAsync(buffer, cancellationToken);
            if (lidos == 0)
            {
                break;
            }

            total += lidos;
            if (total > limite)
            {
                throw ExcecaoAplicacao.ArquivoMuitoGrande(
                    "photo_too_large",
                    "A foto excede o limite de 5 MiB por arquivo.");
            }

            await memoria.WriteAsync(buffer.AsMemory(0, lidos), cancellationToken);
        }

        return memoria.ToArray();
    }
}
