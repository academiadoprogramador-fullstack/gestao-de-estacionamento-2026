namespace Estacionamento.Domain.Autenticacao;

/// <summary>
/// Regras puras de normalização usadas para comparar identidades configuradas
/// sem persistir ou registrar os valores originais.
/// </summary>
public static class NormalizadorEmail
{
    /// <summary>
    /// Normaliza um e-mail para comparação: remove espaços nas extremidades e
    /// converte para minúsculas invariantes. Retorna vazio quando a entrada é nula.
    /// </summary>
    public static string Normalizar(string? email) =>
        string.IsNullOrWhiteSpace(email) ? string.Empty : email.Trim().ToLowerInvariant();
}
