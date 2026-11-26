using Estacionamento.Application.Auditoria;
using Estacionamento.Application.Configuracao;
using Estacionamento.Application.Fotos;
using Estacionamento.Application.Seguranca;
using Estacionamento.Application.Tempo;
using Estacionamento.Domain.Atendimentos;
using Estacionamento.Domain.Fotos;
using Estacionamento.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Estacionamento.Infrastructure.Servicos;

/// <summary>
/// Manutenção durável de fotos em lotes pequenos. Executa no máximo um lote
/// por chamada, sem worker contínuo, e pode ser retomada após reinício.
/// </summary>
public sealed class ServicoManutencaoFotos : IServicoManutencaoFotos
{
    private const int TamanhoLote = 20;

    private readonly EstacionamentoDbContext _db;
    private readonly IArmazenamentoFotos _armazenamento;
    private readonly IAuditoriaNegocio _auditoria;
    private readonly IRelogio _relogio;
    private readonly ArmazenamentoFotosOptions _opcoes;
    private readonly IContextoAplicacao _contexto;
    private readonly ILogger<ServicoManutencaoFotos> _logger;

    public ServicoManutencaoFotos(
        EstacionamentoDbContext db,
        IArmazenamentoFotos armazenamento,
        IAuditoriaNegocio auditoria,
        IRelogio relogio,
        IOptions<ArmazenamentoFotosOptions> opcoes,
        IContextoAplicacao contexto,
        ILogger<ServicoManutencaoFotos> logger)
    {
        _db = db;
        _armazenamento = armazenamento;
        _auditoria = auditoria;
        _relogio = relogio;
        _opcoes = opcoes.Value;
        _contexto = contexto;
        _logger = logger;
    }

    public async Task<int> ExecutarLoteAsync(CancellationToken cancellationToken = default)
    {
        if (!_opcoes.Habilitado)
        {
            return 0;
        }

        var processados = 0;
        processados += await ReconciliarPendentesAsync(cancellationToken);
        processados += await AplicarRetencaoAsync(cancellationToken);
        processados += await LimparAsync(cancellationToken);
        return processados;
    }

    private async Task<int> ReconciliarPendentesAsync(CancellationToken cancellationToken)
    {
        var limite = _relogio.UtcNow.AddDays(-_opcoes.DiasParaReconciliarOrfao);
        var pendentes = await _db.FotosVistoria
            .AsNoTracking()
            .Where(foto => foto.Estado == EstadosFotoVistoria.Pendente &&
                           (foto.ReservadoAteUtc == null || foto.ReservadoAteUtc <= limite))
            .OrderBy(foto => foto.CriadoEmUtc)
            .Take(TamanhoLote)
            .Select(foto => foto.Id)
            .ToListAsync(cancellationToken);

        var processados = 0;
        foreach (var fotoId in pendentes)
        {
            try
            {
                _db.ChangeTracker.Clear();
                var foto = await _db.FotosVistoria
                    .SingleOrDefaultAsync(registro => registro.Id == fotoId, cancellationToken);

                if (foto is null || foto.Estado != EstadosFotoVistoria.Pendente)
                {
                    continue;
                }

                var existe = await _armazenamento.ExisteAsync(foto.ChaveObjeto, cancellationToken);
                if (!existe)
                {
                    await AgendarLimpezaAsync(foto, TiposTarefaFoto.Confirmacao, cancellationToken);
                    continue;
                }

                if (await ConfirmarReconciliadoAsync(foto, cancellationToken))
                {
                    processados += 1;
                }
            }
            catch (Exception excecao)
            {
                _logger.LogWarning(
                    "Reconciliação de foto pendente indisponível. Tipo: {Tipo}.",
                    excecao.GetType().Name);
            }
        }

        return processados;
    }

    private async Task<bool> ConfirmarReconciliadoAsync(
        FotoVistoria foto,
        CancellationToken cancellationToken)
    {
        var atendimento = await _db.Atendimentos
            .FromSqlInterpolated(
                $"SELECT * FROM [Atendimentos] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {foto.AtendimentoId}")
            .SingleOrDefaultAsync(cancellationToken);

        if (atendimento is null ||
            !atendimento.RowVersion.SequenceEqual(foto.VersaoAtendimentoBase) ||
            !await JanelaAbertaAsync(atendimento, foto.Tipo, cancellationToken))
        {
            await AgendarLimpezaAsync(foto, TiposTarefaFoto.Confirmacao, cancellationToken);
            return false;
        }

        var agora = _relogio.UtcNow;
        foto.Estado = EstadosFotoVistoria.Disponivel;
        foto.ConfirmadoEmUtc = agora;
        foto.ReservadoAteUtc = null;

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
            AtorOpaco = foto.AtorId.ToString(),
            OcorridoEmUtc = agora,
            DetalheOpaco = "reconciliacao"
        });

        _auditoria.Adicionar(new EventoAuditoriaNegocio(
            TiposAuditoriaNegocio.FotoAnexada,
            foto.AtorId,
            "foto",
            foto.Id,
            agora,
            null,
            "reconciliacao"));

        var consumo = await ObterConsumoAsync(cancellationToken);
        consumo.BytesReservados = Math.Max(0, consumo.BytesReservados - foto.TamanhoBytes);
        consumo.BytesArmazenados += foto.TamanhoBytes;
        consumo.AtualizadoEmUtc = agora;

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<int> AplicarRetencaoAsync(CancellationToken cancellationToken)
    {
        var disponiveis = await _db.FotosVistoria
            .AsNoTracking()
            .Where(foto => foto.Estado == EstadosFotoVistoria.Disponivel)
            .OrderBy(foto => foto.ConfirmadoEmUtc)
            .Take(TamanhoLote)
            .Select(foto => foto.Id)
            .ToListAsync(cancellationToken);

        var processados = 0;
        foreach (var fotoId in disponiveis)
        {
            try
            {
                _db.ChangeTracker.Clear();
                var foto = await _db.FotosVistoria
                    .SingleOrDefaultAsync(registro => registro.Id == fotoId, cancellationToken);

                if (foto is null || foto.Estado != EstadosFotoVistoria.Disponivel)
                {
                    continue;
                }

                if (!await ApoioFotos.EstaVencidaAsync(
                        _db,
                        foto.AtendimentoId,
                        _opcoes.RetencaoDias,
                        _relogio.UtcNow,
                        cancellationToken))
                {
                    continue;
                }

                foto.Estado = EstadosFotoVistoria.LimpezaPendente;
                foto.ReservadoAteUtc = null;
                await AgendarTarefaAsync(foto, TiposTarefaFoto.Retencao, cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
                processados += 1;
            }
            catch (Exception excecao)
            {
                _logger.LogWarning(
                    "Retenção de foto indisponível. Tipo: {Tipo}.",
                    excecao.GetType().Name);
            }
        }

        return processados;
    }

    private async Task<int> LimparAsync(CancellationToken cancellationToken)
    {
        var agora = _relogio.UtcNow;
        var tarefas = await _db.TarefasFotos
            .Where(tarefa => tarefa.Estado == "pendente" &&
                             (tarefa.LeaseAteUtc == null || tarefa.LeaseAteUtc <= agora))
            .OrderBy(tarefa => tarefa.CriadoEmUtc)
            .Take(TamanhoLote)
            .ToListAsync(cancellationToken);

        var processados = 0;
        foreach (var tarefa in tarefas)
        {
            try
            {
                tarefa.LeaseAteUtc = agora.AddMinutes(5);
                tarefa.Tentativas += 1;
                await _db.SaveChangesAsync(cancellationToken);

                var foto = await _db.FotosVistoria
                    .SingleOrDefaultAsync(registro => registro.Id == tarefa.FotoId, cancellationToken);

                if (foto is null)
                {
                    tarefa.Estado = "concluida";
                    await _db.SaveChangesAsync(cancellationToken);
                    continue;
                }

                await _armazenamento.ExcluirAsync(foto.ChaveObjeto, cancellationToken);

                foto.Estado = EstadosFotoVistoria.Descartada;
                foto.ReservadoAteUtc = null;
                tarefa.Estado = "concluida";
                tarefa.LeaseAteUtc = null;

                _db.EventosAtendimento.Add(new EventoAtendimento
                {
                    AtendimentoId = foto.AtendimentoId,
                    Tipo = TiposEventoAtendimento.FotoDescartada,
                    AtorOpaco = foto.AtorId.ToString(),
                    OcorridoEmUtc = agora,
                    DetalheOpaco = tarefa.Tipo
                });

                _auditoria.Adicionar(new EventoAuditoriaNegocio(
                    TiposAuditoriaNegocio.FotoDescartada,
                    foto.AtorId,
                    "foto",
                    foto.Id,
                    agora,
                    null,
                    tarefa.Tipo));

                var consumo = await ObterConsumoAsync(cancellationToken);
                if (tarefa.Tipo == TiposTarefaFoto.Retencao)
                {
                    consumo.BytesArmazenados = Math.Max(0, consumo.BytesArmazenados - foto.TamanhoBytes);
                }
                else
                {
                    consumo.BytesReservados = Math.Max(0, consumo.BytesReservados - foto.TamanhoBytes);
                }

                consumo.AtualizadoEmUtc = agora;
                await _db.SaveChangesAsync(cancellationToken);
                processados += 1;
            }
            catch (Exception excecao)
            {
                tarefa.Estado = "pendente";
                tarefa.LeaseAteUtc = null;
                tarefa.UltimoErroCodigo = excecao is Application.Comum.ExcecaoAplicacao aplicacao
                    ? aplicacao.Codigo
                    : "photo_cleanup_failed";

                try
                {
                    await _db.SaveChangesAsync(cancellationToken);
                }
                catch
                {
                    _db.ChangeTracker.Clear();
                }

                _logger.LogWarning(
                    "Limpeza de foto indisponível. Tipo: {Tipo}.",
                    excecao.GetType().Name);
            }
        }

        return processados;
    }

    private async Task AgendarLimpezaAsync(
        FotoVistoria foto,
        string origem,
        CancellationToken cancellationToken)
    {
        foto.Estado = EstadosFotoVistoria.LimpezaPendente;
        foto.ReservadoAteUtc = null;
        await AgendarTarefaAsync(foto, TiposTarefaFoto.Limpeza, cancellationToken, origem);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task AgendarTarefaAsync(
        FotoVistoria foto,
        string tipo,
        CancellationToken cancellationToken,
        string? detalhe = null)
    {
        if (await _db.TarefasFotos.AnyAsync(
                tarefa => tarefa.FotoId == foto.Id &&
                          tarefa.Tipo == tipo &&
                          tarefa.Estado == "pendente",
                cancellationToken))
        {
            return;
        }

        _db.TarefasFotos.Add(new TarefaFoto
        {
            Id = Guid.NewGuid(),
            FotoId = foto.Id,
            Tipo = tipo,
            Estado = "pendente",
            CriadoEmUtc = _relogio.UtcNow,
            UltimoErroCodigo = detalhe
        });
    }

    private async Task<ConsumoFotosMensal> ObterConsumoAsync(CancellationToken cancellationToken)
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

    private async Task<bool> JanelaAbertaAsync(
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

        return string.Equals(tipo, TiposVistoriaFoto.Entrada, StringComparison.Ordinal)
            ? vistoriaEntrada && RegraFotosVistoria.EntradaAberta(temPedido, entregue)
            : RegraFotosVistoria.SaidaAberta(vistoriaSaida, entregue);
    }
}
