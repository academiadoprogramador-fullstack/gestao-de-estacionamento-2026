namespace Estacionamento.Domain.Vagas;

/// <summary>
/// Regras puras de normalização e validação do código de vaga. O código é
/// normalizado com trim e maiúsculas invariantes e limitado a 32 caracteres.
/// </summary>
public static class CodigoVaga
{
    public const int TamanhoMaximo = 32;

    /// <summary>Remove espaços nas extremidades e converte para maiúsculas.</summary>
    public static string Normalizar(string? codigo) => (codigo ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>Um código válido possui de 1 a 32 caracteres após a normalização.</summary>
    public static bool EhValido(string normalizado) =>
        normalizado.Length is >= 1 and <= TamanhoMaximo;
}
