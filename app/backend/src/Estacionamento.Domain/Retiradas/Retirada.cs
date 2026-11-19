namespace Estacionamento.Domain.Retiradas;

/// <summary>
/// Resultado da conferência presencial entre o documento com foto apresentado
/// e o nome/número registrados na entrada. Nenhuma foto ou novo número é
/// coletado.
/// </summary>
public static class ConferenciaRetirada
{
    public const string Aprovada = "aprovada";
    public const string Divergente = "divergente";

    public static readonly IReadOnlySet<string> Valores =
        new HashSet<string>(StringComparer.Ordinal)
        {
            Aprovada,
            Divergente
        };

    public static bool EhValida(string? valor) =>
        valor is not null && Valores.Contains(valor);
}

/// <summary>
/// Pedido de retirada único por atendimento no ciclo do MVP. O horário e o
/// autor são sempre do servidor. Não há cancelamento ou reabertura.
/// </summary>
public sealed class PedidoRetirada
{
    public Guid Id { get; set; }
    public Guid AtendimentoId { get; set; }
    public string Conferencia { get; set; } = ConferenciaRetirada.Aprovada;

    /// <summary>Declaração obrigatória de presença do titular.</summary>
    public bool TitularPresente { get; set; }

    public string Estado { get; set; } = Estacionamento.Domain.Atendimentos.EstadoAtendimento.Aguardando;
    public DateTimeOffset SolicitadoEmUtc { get; set; }
    public Guid SolicitadoPorId { get; set; }
    public DateTimeOffset? BuscaIniciadaEmUtc { get; set; }
    public Guid? BuscaPorId { get; set; }

    /// <summary>Versão de concorrência do pedido.</summary>
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// Cobrança fixada no instante do pedido. Imutável: descontos não a editam e
/// publicações posteriores de tarifa não alteram o snapshot.
/// </summary>
public sealed class Cobranca
{
    public Guid Id { get; set; }
    public Guid PedidoId { get; set; }
    public long DuracaoTicks { get; set; }
    public int Diarias { get; set; }
    public Guid TarifaVersaoId { get; set; }
    public decimal TarifaValorSnapshot { get; set; }
    public decimal TotalOriginal { get; set; }
    public DateTimeOffset CriadaEmUtc { get; set; }
}

/// <summary>
/// Cabeçalho financeiro único por atendimento. Toda alteração financeira
/// bloqueia este registro, relê os lançamentos e incrementa a revisão na mesma
/// transação.
/// </summary>
public sealed class RazaoAtendimento
{
    public Guid Id { get; set; }
    public Guid AtendimentoId { get; set; }
    public int Revisao { get; set; } = 1;
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>Tipos de lançamento financeiro append-only.</summary>
public static class TipoLancamentoFinanceiro
{
    public const string Desconto = "desconto";
    public const string Pagamento = "pagamento";
    public const string Estorno = "estorno";

    public static readonly IReadOnlySet<string> Valores =
        new HashSet<string>(StringComparer.Ordinal)
        {
            Desconto,
            Pagamento,
            Estorno
        };
}

/// <summary>Formas de pagamento aceitas nesta fase.</summary>
public static class FormaPagamento
{
    public const string Dinheiro = "dinheiro";
    public const string Cartao = "cartao";
    public const string Pix = "pix";

    public static readonly IReadOnlySet<string> Valores =
        new HashSet<string>(StringComparer.Ordinal)
        {
            Dinheiro,
            Cartao,
            Pix
        };

    public static bool EhValida(string? valor) => valor is not null && Valores.Contains(valor);
}

/// <summary>
/// Lançamento financeiro append-only com discriminador de desconto, pagamento
/// ou estorno. Checks garantem valor positivo, forma apenas em pagamento,
/// vínculo apenas em estorno e justificativa apenas em ajustes.
/// </summary>
public sealed class LancamentoFinanceiro
{
    public Guid Id { get; set; }
    public Guid AtendimentoId { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public decimal Valor { get; set; }
    public string? FormaPagamento { get; set; }
    public Guid? PagamentoOriginalId { get; set; }
    public byte[]? JustificativaCifrada { get; set; }
    public Guid AtorId { get; set; }
    public DateTimeOffset OcorridoEmUtc { get; set; }
}

/// <summary>Tipos de autorização de exceção, independentes entre si.</summary>
public static class TipoAutorizacaoRetirada
{
    public const string Documental = "documental";
    public const string Financeira = "financeira";

    public static readonly IReadOnlySet<string> Valores =
        new HashSet<string>(StringComparer.Ordinal)
        {
            Documental,
            Financeira
        };

    public static bool EhValido(string? valor) => valor is not null && Valores.Contains(valor);
}

/// <summary>
/// Concessão de autorização de exceção, vinculada ao pedido e à versão
/// relevante (conferência para documental, revisão da razão para financeira).
/// </summary>
public sealed class ConcessaoAutorizacao
{
    public Guid Id { get; set; }
    public Guid PedidoId { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public int RevisaoReferencia { get; set; }
    public byte[] JustificativaCifrada { get; set; } = [];
    public Guid ConcedidaPorId { get; set; }
    public DateTimeOffset ConcedidaEmUtc { get; set; }
}

/// <summary>Revogação única por concessão, append-only.</summary>
public sealed class RevogacaoAutorizacao
{
    public Guid Id { get; set; }
    public Guid ConcessaoId { get; set; }
    public byte[] JustificativaCifrada { get; set; } = [];
    public Guid RevogadaPorId { get; set; }
    public DateTimeOffset RevogadaEmUtc { get; set; }
}

/// <summary>Vigência derivada de autorização, nunca persistida.</summary>
public static class VigenciaAutorizacao
{
    public const string Vigente = "vigente";
    public const string Revogada = "revogada";
    public const string Substituida = "substituida";
    public const string Obsoleta = "obsoleta";
    public const string NaoConcedida = "nao_concedida";
}
