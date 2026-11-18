namespace Estacionamento.Domain.Atendimentos;

/// <summary>
/// Limites técnicos dos textos obrigatórios da entrada. Todos são aplicados
/// após remover espaços nas extremidades.
/// </summary>
public static class LimitesTextoAtendimento
{
    public const int Modelo = 120;
    public const int Titular = 200;
    public const int Documento = 80;
    public const int Vistoria = 4000;

    public static string Normalizar(string? texto) => (texto ?? string.Empty).Trim();

    public static bool EhValido(string normalizado, int tamanhoMaximo) =>
        normalizado.Length is >= 1 && normalizado.Length <= tamanhoMaximo;
}
