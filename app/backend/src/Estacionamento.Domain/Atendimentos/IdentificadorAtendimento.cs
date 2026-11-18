using System.Text.RegularExpressions;

namespace Estacionamento.Domain.Atendimentos;

/// <summary>
/// Identificador público legível gerado pelo servidor no formato
/// <c>AT-{anoUTC}-{sequência:D6}</c>. Não é a chave primária nem é sequencial
/// por unidade.
/// </summary>
public static partial class IdentificadorAtendimento
{
    public const string Prefixo = "AT";

    public static string Formatar(int anoUtc, long sequencia) =>
        $"{Prefixo}-{anoUtc:D4}-{sequencia:D6}";

    public static bool EhValido(string? identificador) =>
        !string.IsNullOrWhiteSpace(identificador) && Padrao().IsMatch(identificador.Trim());

    /// <summary>Normaliza o valor informado na busca exata por identificador.</summary>
    public static string Normalizar(string? identificador) =>
        (identificador ?? string.Empty).Trim().ToUpperInvariant();

    [GeneratedRegex("^AT-[0-9]{4}-[0-9]{6,}$", RegexOptions.CultureInvariant)]
    private static partial Regex Padrao();
}
