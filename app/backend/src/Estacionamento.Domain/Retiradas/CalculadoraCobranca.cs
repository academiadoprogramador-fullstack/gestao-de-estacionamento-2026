namespace Estacionamento.Domain.Retiradas;

/// <summary>
/// Cálculo puro da permanência em ticks UTC. Conta períodos iniciados de 24
/// horas com mínimo de uma diária, sem ponto flutuante, arredondamento ou
/// truncamento: exatamente 24 h = 1; 24 h + 1 tick = 2; 48 h = 2.
/// </summary>
public static class CalculadoraCobranca
{
    public static ResultadoCalculoCobranca Calcular(
        DateTimeOffset entradaUtc,
        DateTimeOffset pedidoUtc,
        decimal tarifaValorSnapshot)
    {
        var duracaoTicks = pedidoUtc.UtcTicks - entradaUtc.UtcTicks;

        if (duracaoTicks < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pedidoUtc),
                "O instante do pedido não pode ser anterior à entrada.");
        }

        if (tarifaValorSnapshot < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tarifaValorSnapshot),
                "O valor da tarifa não pode ser negativo.");
        }

        // Teto inteiro de períodos de 24 h sem conversão para ponto flutuante.
        var diarias = duracaoTicks == 0
            ? 1
            : Math.Max(1, (duracaoTicks + TimeSpan.TicksPerDay - 1) / TimeSpan.TicksPerDay);

        if (diarias > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pedidoUtc),
                "A quantidade de diárias excede o limite representável.");
        }

        decimal total;
        try
        {
            total = checked(diarias * tarifaValorSnapshot);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tarifaValorSnapshot),
                "O total calculado excede o limite representável.");
        }

        if (total < 0 || total > 9_999_999_999_999_999.99m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tarifaValorSnapshot),
                "O total calculado excede o limite de decimal(18,2).");
        }

        var totalArredondado = decimal.Round(total, 2, MidpointRounding.AwayFromZero);
        return new ResultadoCalculoCobranca(
            duracaoTicks,
            (int)diarias,
            totalArredondado);
    }
}

/// <summary>Resultado imutável do cálculo de permanência.</summary>
public sealed record ResultadoCalculoCobranca(
    long DuracaoTicks,
    int Diarias,
    decimal TotalOriginal);

/// <summary>
/// Composição auditável do saldo a partir dos lançamentos append-only:
/// <c>cobrado = total original − descontos</c>,
/// <c>pago líquido = pagamentos − estornos</c> e
/// <c>saldo = cobrado − pago líquido</c>. O saldo por atendimento nunca é
/// negativo; uma cobrança zerada não admite pagamento líquido.
/// </summary>
public static class ComposicaoSaldo
{
    public static ResultadoComposicaoSaldo Calcular(
        decimal totalOriginal,
        IEnumerable<LancamentoFinanceiro> lancamentos)
    {
        decimal descontos = 0;
        decimal pagamentos = 0;
        decimal estornos = 0;

        foreach (var lancamento in lancamentos)
        {
            switch (lancamento.Tipo)
            {
                case TipoLancamentoFinanceiro.Desconto:
                    descontos += lancamento.Valor;
                    break;
                case TipoLancamentoFinanceiro.Pagamento:
                    pagamentos += lancamento.Valor;
                    break;
                case TipoLancamentoFinanceiro.Estorno:
                    estornos += lancamento.Valor;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Tipo de lançamento desconhecido na composição do saldo: {lancamento.Tipo}.");
            }
        }

        var cobrado = totalOriginal - descontos;
        var pagoLiquido = pagamentos - estornos;
        var saldo = cobrado - pagoLiquido;

        if (cobrado < 0)
        {
            throw new InvalidOperationException(
                "Os descontos ultrapassam o total original do atendimento.");
        }

        if (pagoLiquido < 0)
        {
            throw new InvalidOperationException(
                "Os estornos ultrapassam os pagamentos do atendimento.");
        }

        if (saldo < 0)
        {
            throw new InvalidOperationException(
                "O saldo do atendimento não pode ser negativo.");
        }

        if (cobrado == 0 && pagoLiquido > 0)
        {
            throw new InvalidOperationException(
                "Cobrança zerada não admite pagamento líquido.");
        }

        return new ResultadoComposicaoSaldo(
            descontos,
            pagamentos,
            estornos,
            cobrado,
            pagoLiquido,
            saldo);
    }

    /// <summary>
    /// Deriva a vigência de uma concessão sem persistir estado mutável.
    /// Precedência: revogada &gt; substituída &gt; obsoleta &gt; vigente.
    /// </summary>
    public static string DerivarVigencia(
        ConcessaoAutorizacao concessao,
        bool ehUltimaDoTipo,
        bool revogada,
        int revisaoCorrenteRazao)
    {
        if (revogada)
        {
            return VigenciaAutorizacao.Revogada;
        }

        if (!ehUltimaDoTipo)
        {
            return VigenciaAutorizacao.Substituida;
        }

        if (concessao.Tipo == TipoAutorizacaoRetirada.Financeira &&
            concessao.RevisaoReferencia != revisaoCorrenteRazao)
        {
            return VigenciaAutorizacao.Obsoleta;
        }

        return VigenciaAutorizacao.Vigente;
    }
}

/// <summary>Valores derivados dos lançamentos financeiros.</summary>
public sealed record ResultadoComposicaoSaldo(
    decimal Descontos,
    decimal Pagamentos,
    decimal Estornos,
    decimal Cobrado,
    decimal PagoLiquido,
    decimal Saldo);
