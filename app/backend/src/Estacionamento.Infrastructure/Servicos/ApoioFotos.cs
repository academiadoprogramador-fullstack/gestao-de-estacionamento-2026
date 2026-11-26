using System.Globalization;
using System.Text.Json;
using Estacionamento.Application.Configuracao;
using Estacionamento.Application.Fotos;
using Estacionamento.Domain.Atendimentos;
using Estacionamento.Domain.Fotos;
using Estacionamento.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Estacionamento.Infrastructure.Servicos;

/// <summary>Utilitários compartilhados de orçamento, retenção e recibos.</summary>
internal static class ApoioFotos
{
    public static string Competencia(DateTimeOffset instante) =>
        instante.UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public static async Task<ConsumoTotalFotos> ObterConsumoTotalAsync(
        EstacionamentoDbContext db,
        string competencia,
        CancellationToken cancellationToken)
    {
        var linhas = await db.ConsumosFotosMensais
            .AsNoTracking()
            .Where(consumo => consumo.Competencia == competencia)
            .ToListAsync(cancellationToken);

        return new ConsumoTotalFotos(
            linhas.Sum(linha => linha.BytesArmazenados),
            linhas.Sum(linha => linha.BytesTransferidos),
            linhas.Sum(linha => linha.OperacoesClasseA),
            linhas.Sum(linha => linha.OperacoesClasseB),
            linhas.Sum(linha => linha.BytesReservados),
            linhas.Any(linha =>
                string.Equals(linha.EstadoReconciliacao, "desconhecido", StringComparison.Ordinal)));
    }

    public static OrcamentoFotosResumo Resumir(
        ConsumoTotalFotos consumo,
        ArmazenamentoFotosOptions opcoes)
    {
        var percentualBytes = Percentual(
            consumo.BytesArmazenados + consumo.BytesReservados,
            opcoes.Orcamento.FranquiaBytesMensal);
        var percentualA = Percentual(
            consumo.OperacoesClasseA,
            opcoes.Orcamento.FranquiaOperacoesClasseAMensal);
        var percentualB = Percentual(
            consumo.OperacoesClasseB,
            opcoes.Orcamento.FranquiaOperacoesClasseBMensal);

        var desconhecido = consumo.ConsumoDesconhecido;
        var bloqueado = desconhecido ||
            percentualBytes >= opcoes.Orcamento.BloqueioPercentual ||
            percentualA >= opcoes.Orcamento.BloqueioPercentual ||
            percentualB >= opcoes.Orcamento.BloqueioPercentual;
        var aviso = !bloqueado &&
            (percentualBytes >= opcoes.Orcamento.AvisoPercentual ||
             percentualA >= opcoes.Orcamento.AvisoPercentual ||
             percentualB >= opcoes.Orcamento.AvisoPercentual);

        var motivo = desconhecido
            ? "consumo_desconhecido"
            : bloqueado
                ? "limite_orcamento"
                : null;

        return new OrcamentoFotosResumo(
            percentualBytes,
            percentualA,
            percentualB,
            aviso,
            bloqueado,
            motivo,
            opcoes.Limites.MaximoFotosPorVistoria);
    }

    public static void GarantirOrcamentoDisponivel(
        ConsumoTotalFotos consumo,
        ArmazenamentoFotosOptions opcoes,
        long bytesAdicionais,
        long operacoesClasseAAdicionais,
        long operacoesClasseBAdicionais)
    {
        if (consumo.ConsumoDesconhecido)
        {
            throw Application.Comum.ExcecaoAplicacao.Indisponivel(
                "photo_quota_unknown",
                "O consumo de fotos está desconhecido. As fotos ficam suspensas até a reconciliação.");
        }

        var resumo = Resumir(consumo, opcoes);
        if (resumo.Suspenso)
        {
            throw Application.Comum.ExcecaoAplicacao.Indisponivel(
                "photo_quota_exceeded",
                "O orçamento de fotos atingiu o limite de segurança. Tente novamente após a reconciliação.");
        }

        var bytesProjetados = consumo.BytesArmazenados + consumo.BytesReservados + bytesAdicionais;
        var aProjetadas = consumo.OperacoesClasseA + operacoesClasseAAdicionais;
        var bProjetadas = consumo.OperacoesClasseB + operacoesClasseBAdicionais;

        if (Percentual(bytesProjetados, opcoes.Orcamento.FranquiaBytesMensal) > opcoes.Orcamento.BloqueioPercentual ||
            Percentual(aProjetadas, opcoes.Orcamento.FranquiaOperacoesClasseAMensal) > opcoes.Orcamento.BloqueioPercentual ||
            Percentual(bProjetadas, opcoes.Orcamento.FranquiaOperacoesClasseBMensal) > opcoes.Orcamento.BloqueioPercentual)
        {
            throw Application.Comum.ExcecaoAplicacao.Indisponivel(
                "photo_quota_exceeded",
                "Esta operação ultrapassaria o limite de segurança do orçamento de fotos.");
        }
    }

    public static async Task<bool> EstaVencidaAsync(
        EstacionamentoDbContext db,
        Guid atendimentoId,
        int retencaoDias,
        DateTimeOffset agora,
        CancellationToken cancellationToken)
    {
        var atendimento = await db.Atendimentos
            .AsNoTracking()
            .SingleOrDefaultAsync(registro => registro.Id == atendimentoId, cancellationToken);

        if (atendimento is null ||
            !string.Equals(atendimento.Estado, EstadoAtendimento.Entregue, StringComparison.Ordinal))
        {
            return false;
        }

        var entrega = await db.EntregasVeiculo
            .AsNoTracking()
            .SingleOrDefaultAsync(registro => registro.AtendimentoId == atendimentoId, cancellationToken);

        if (entrega is null)
        {
            return false;
        }

        var cobranca = await db.Cobrancas
            .AsNoTracking()
            .Join(
                db.PedidosRetirada.AsNoTracking(),
                cobranca => cobranca.PedidoId,
                pedido => pedido.Id,
                (cobranca, pedido) => new { cobranca, pedido })
            .Where(item => item.pedido.AtendimentoId == atendimentoId)
            .Select(item => item.cobranca)
            .SingleOrDefaultAsync(cancellationToken);

        var totalOriginal = cobranca?.TotalOriginal ?? 0m;
        var lancamentos = await db.LancamentosFinanceiros
            .AsNoTracking()
            .Where(lancamento => lancamento.AtendimentoId == atendimentoId)
            .Select(lancamento => new { lancamento.Tipo, lancamento.Valor, lancamento.OcorridoEmUtc })
            .ToListAsync(cancellationToken);

        var descontos = lancamentos
            .Where(lancamento => string.Equals(lancamento.Tipo, "desconto", StringComparison.Ordinal))
            .Sum(lancamento => lancamento.Valor);
        var pagamentos = lancamentos
            .Where(lancamento => string.Equals(lancamento.Tipo, "pagamento", StringComparison.Ordinal))
            .Sum(lancamento => lancamento.Valor);
        var estornos = lancamentos
            .Where(lancamento => string.Equals(lancamento.Tipo, "estorno", StringComparison.Ordinal))
            .Sum(lancamento => lancamento.Valor);

        var cobrado = totalOriginal - descontos;
        var pagoLiquido = pagamentos - estornos;
        if (cobrado - pagoLiquido != 0m)
        {
            return false;
        }

        var marco = entrega.EntregueEmUtc;
        if (lancamentos.Count > 0)
        {
            var ultimoLancamento = lancamentos.Max(lancamento => lancamento.OcorridoEmUtc);
            if (ultimoLancamento > marco)
            {
                marco = ultimoLancamento;
            }
        }

        return marco <= agora.AddDays(-retencaoDias);
    }

    public static string Location(Guid atendimentoId, string tipo, Guid fotoId) =>
        $"/api/v1/attendances/{atendimentoId}/inspections/{tipo}/photos/{fotoId}";

    public static ResultadoFoto MontarResultado(FotoVistoria foto) =>
        new(
            foto.Id,
            foto.AtendimentoId,
            foto.Tipo,
            foto.Estado,
            foto.Mime,
            foto.TamanhoBytes,
            foto.Largura,
            foto.Altura,
            foto.CriadoEmUtc,
            Location(foto.AtendimentoId, foto.Tipo, foto.Id));

    public static string SerializarResultado(ResultadoFoto resultado) =>
        JsonSerializer.Serialize(resultado, JsonSerializerOptions.Web);

    public static ResultadoFoto DesserializarResultado(string json) =>
        JsonSerializer.Deserialize<ResultadoFoto>(json, JsonSerializerOptions.Web)
        ?? throw new InvalidOperationException("Resultado idempotente de foto inválido.");

    private static int Percentual(long consumo, long franquia) =>
        franquia <= 0 ? 100 : (int)Math.Min(100, Math.Round(consumo * 100d / franquia, MidpointRounding.AwayFromZero));
}

/// <summary>Somatório do consumo mensal de todos os ambientes registrados.</summary>
internal sealed record ConsumoTotalFotos(
    long BytesArmazenados,
    long BytesTransferidos,
    long OperacoesClasseA,
    long OperacoesClasseB,
    long BytesReservados,
    bool ConsumoDesconhecido);
