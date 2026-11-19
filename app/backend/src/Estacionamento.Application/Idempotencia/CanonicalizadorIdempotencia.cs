using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Estacionamento.Domain.Idempotencia;

namespace Estacionamento.Application.Idempotencia;

/// <summary>
/// Representação canônica determinística do conteúdo de comandos idempotentes.
/// A serialização é ordenada, com comprimento explícito por campo, para evitar
/// ambiguidade; o resultado é o SHA-256 binário de 32 bytes. Nenhum corpo de
/// requisição, segredo ou conteúdo sensível é persistido no resultado.
/// </summary>
public static class CanonicalizadorIdempotencia
{
    public const int VersaoAtual = 1;

    /// <summary>
    /// Calcula o hash de uma lista de campos nomeados. Os nomes são ordenados
    /// ordinalmente, de modo que a ordem de chamada não altera o resultado.
    /// </summary>
    public static byte[] CalcularHash(
        int versao,
        string operacao,
        Guid atorId,
        IEnumerable<KeyValuePair<string, string>> campos)
    {
        var construtor = new StringBuilder();
        construtor.Append("idempotencia-v").Append(versao).Append('\n');

        Adicionar(construtor, "operacao", operacao);
        Adicionar(construtor, "ator", atorId.ToString("N"));

        foreach (var campo in campos.OrderBy(par => par.Key, StringComparer.Ordinal))
        {
            Adicionar(construtor, campo.Key, campo.Value);
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(construtor.ToString()));
    }

    /// <summary>Hash do check-in com os valores já normalizados.</summary>
    public static byte[] CalcularHashEntradaV1(
        Guid atorId,
        Guid vagaId,
        string placaNormalizada,
        string modelo,
        string titular,
        string documento,
        string vistoria)
    {
        var campos = new[]
        {
            new KeyValuePair<string, string>("alvo.vaga", vagaId.ToString("N")),
            new KeyValuePair<string, string>("documento", documento),
            new KeyValuePair<string, string>("modelo", modelo),
            new KeyValuePair<string, string>("placa", placaNormalizada),
            new KeyValuePair<string, string>("titular", titular),
            new KeyValuePair<string, string>("vistoria", vistoria)
        };

        return CalcularHash(
            VersaoAtual,
            ResultadoIdempotente.OperacaoRegistrarEntrada,
            atorId,
            campos);
    }

    /// <summary>Hash do pedido de retirada (atendimento, presença e conferência).</summary>
    public static byte[] CalcularHashSolicitacaoRetiradaV1(
        Guid atorId,
        Guid atendimentoId,
        bool titularPresente,
        string conferencia) =>
        CalcularHash(
            VersaoAtual,
            ResultadoIdempotente.OperacaoSolicitarRetirada,
            atorId,
            new[]
            {
                new KeyValuePair<string, string>("alvo.atendimento", atendimentoId.ToString("N")),
                new KeyValuePair<string, string>("conferencia", conferencia),
                new KeyValuePair<string, string>("titularPresente", titularPresente ? "true" : "false")
            });

    /// <summary>Hash da concessão de autorização documental ou financeira.</summary>
    public static byte[] CalcularHashConcessaoAutorizacaoV1(
        Guid atorId,
        Guid pedidoId,
        string tipo,
        byte[] versaoEsperada,
        string justificativa) =>
        CalcularHash(
            VersaoAtual,
            ResultadoIdempotente.OperacaoConcederAutorizacao,
            atorId,
            new[]
            {
                new KeyValuePair<string, string>("alvo.pedido", pedidoId.ToString("N")),
                new KeyValuePair<string, string>("justificativa", justificativa),
                new KeyValuePair<string, string>("tipo", tipo),
                new KeyValuePair<string, string>("versaoEsperada", Converter(versaoEsperada))
            });

    /// <summary>Hash da revogação de autorização.</summary>
    public static byte[] CalcularHashRevogacaoAutorizacaoV1(
        Guid atorId,
        Guid pedidoId,
        Guid autorizacaoId,
        string justificativa) =>
        CalcularHash(
            VersaoAtual,
            ResultadoIdempotente.OperacaoRevogarAutorizacao,
            atorId,
            new[]
            {
                new KeyValuePair<string, string>("alvo.pedido", pedidoId.ToString("N")),
                new KeyValuePair<string, string>("autorizacao", autorizacaoId.ToString("N")),
                new KeyValuePair<string, string>("justificativa", justificativa)
            });

    /// <summary>Hash do acerto integral.</summary>
    public static byte[] CalcularHashPagamentoV1(
        Guid atorId,
        Guid pedidoId,
        decimal valor,
        string forma,
        int revisaoEsperada) =>
        CalcularHash(
            VersaoAtual,
            ResultadoIdempotente.OperacaoPagamento,
            atorId,
            new[]
            {
                new KeyValuePair<string, string>("alvo.pedido", pedidoId.ToString("N")),
                new KeyValuePair<string, string>("forma", forma),
                new KeyValuePair<string, string>("revisaoEsperada", revisaoEsperada.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("valor", FormatarValor(valor))
            });

    /// <summary>Hash do desconto administrativo.</summary>
    public static byte[] CalcularHashDescontoV1(
        Guid atorId,
        Guid pedidoId,
        decimal valor,
        string justificativa,
        int revisaoEsperada) =>
        CalcularHash(
            VersaoAtual,
            ResultadoIdempotente.OperacaoDesconto,
            atorId,
            new[]
            {
                new KeyValuePair<string, string>("alvo.pedido", pedidoId.ToString("N")),
                new KeyValuePair<string, string>("justificativa", justificativa),
                new KeyValuePair<string, string>("revisaoEsperada", revisaoEsperada.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("valor", FormatarValor(valor))
            });

    /// <summary>Hash do estorno integral.</summary>
    public static byte[] CalcularHashEstornoV1(
        Guid atorId,
        Guid pedidoId,
        Guid pagamentoId,
        decimal valor,
        string justificativa,
        int revisaoEsperada) =>
        CalcularHash(
            VersaoAtual,
            ResultadoIdempotente.OperacaoEstorno,
            atorId,
            new[]
            {
                new KeyValuePair<string, string>("alvo.pedido", pedidoId.ToString("N")),
                new KeyValuePair<string, string>("justificativa", justificativa),
                new KeyValuePair<string, string>("pagamento", pagamentoId.ToString("N")),
                new KeyValuePair<string, string>("revisaoEsperada", revisaoEsperada.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("valor", FormatarValor(valor))
            });

    private static string FormatarValor(decimal valor) =>
        valor.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Converter(byte[] valor) =>
        valor.Length == 0 ? string.Empty : Convert.ToBase64String(valor);

    private static void Adicionar(StringBuilder construtor, string nome, string valor)
    {
        var bytes = Encoding.UTF8.GetByteCount(valor);
        construtor
            .Append(nome)
            .Append(':')
            .Append(bytes.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(valor)
            .Append('\n');
    }
}
