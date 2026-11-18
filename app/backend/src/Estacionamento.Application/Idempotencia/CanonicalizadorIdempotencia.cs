using System.Security.Cryptography;
using System.Text;
using Estacionamento.Domain.Atendimentos;

namespace Estacionamento.Application.Idempotencia;

/// <summary>
/// Representação canônica determinística do conteúdo de comandos idempotentes.
/// A serialização é ordenada, com comprimento explícito por campo, para evitar
/// ambiguidade; o resultado é o SHA-256 binário de 32 bytes. Nenhum corpo de
/// requisição, segredo ou conteúdo sensível é persistido.
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
            Domain.Idempotencia.ResultadoIdempotente.OperacaoRegistrarEntrada,
            atorId,
            campos);
    }

    private static void Adicionar(StringBuilder construtor, string nome, string valor)
    {
        var bytes = Encoding.UTF8.GetByteCount(valor);
        construtor
            .Append(nome)
            .Append(':')
            .Append(bytes.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append(':')
            .Append(valor)
            .Append('\n');
    }
}
