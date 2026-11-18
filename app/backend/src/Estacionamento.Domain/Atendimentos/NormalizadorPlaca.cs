namespace Estacionamento.Domain.Atendimentos;

/// <summary>
/// Normalização de placa: remove espaços e hífens e converte para maiúsculas.
/// Aceita de 1 a 16 caracteres ASCII alfanuméricos, sem exigir padrão
/// brasileiro e sem unicidade histórica.
/// </summary>
public static class NormalizadorPlaca
{
    public const int TamanhoMaximo = 16;

    public static string Normalizar(string? placa)
    {
        var texto = placa ?? string.Empty;
        var resultado = new char[texto.Length];
        var indice = 0;

        foreach (var caractere in texto)
        {
            if (char.IsWhiteSpace(caractere) || caractere == '-')
            {
                continue;
            }

            resultado[indice++] = char.ToUpperInvariant(caractere);
        }

        return new string(resultado, 0, indice);
    }

    public static bool EhValido(string normalizada)
    {
        if (normalizada.Length is < 1 or > TamanhoMaximo)
        {
            return false;
        }

        foreach (var caractere in normalizada)
        {
            var ascii = caractere is >= 'A' and <= 'Z' or >= '0' and <= '9';
            if (!ascii)
            {
                return false;
            }
        }

        return true;
    }
}
