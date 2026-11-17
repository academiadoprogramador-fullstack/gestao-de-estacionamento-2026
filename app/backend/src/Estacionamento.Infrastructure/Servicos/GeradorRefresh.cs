using System.Security.Cryptography;
using System.Text;
using Estacionamento.Application.Autenticacao;

namespace Estacionamento.Infrastructure.Servicos;

/// <summary>
/// Gera refresh opacos com 32 bytes aleatórios em Base64Url e calcula o hash
/// SHA-256 persistido. O token puro nunca é gravado no SQL nem em log.
/// </summary>
public sealed class GeradorRefresh : IGeradorRefresh
{
    public const int TamanhoEmBytes = 32;

    public string GerarTokenOpaco()
    {
        var bytes = RandomNumberGenerator.GetBytes(TamanhoEmBytes);
        return Base64UrlEncode(bytes);
    }

    public string CalcularHash(string tokenOpaco)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(tokenOpaco));
        return Convert.ToHexStringLower(hash);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
