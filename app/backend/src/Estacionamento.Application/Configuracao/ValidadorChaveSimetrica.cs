using System.Security.Cryptography;

namespace Estacionamento.Application.Configuracao;

/// <summary>
/// Utilitário de validação de material simétrico. A chave deve ser Base64 que
/// decodifica exatamente 32 bytes; a representação textual nunca é registrada.
/// </summary>
public static class ValidadorChaveSimetrica
{
    public const int TamanhoEmBytes = 32;

    public static bool PossuiFormatoValido(string? valorBase64)
    {
        if (string.IsNullOrWhiteSpace(valorBase64))
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[512];
        if (!Convert.TryFromBase64String(valorBase64, buffer, out var bytesWritten))
        {
            return false;
        }

        return bytesWritten == TamanhoEmBytes;
    }

    public static byte[] Decodificar(string valorBase64)
    {
        var bytes = Convert.FromBase64String(valorBase64);
        if (bytes.Length != TamanhoEmBytes)
        {
            throw new InvalidOperationException("A chave simétrica deve ter exatamente 32 bytes.");
        }

        return bytes;
    }
}
