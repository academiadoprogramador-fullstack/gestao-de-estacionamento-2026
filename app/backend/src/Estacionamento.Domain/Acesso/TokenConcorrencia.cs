namespace Estacionamento.Domain.Acesso;

/// <summary>
/// Conversão do token opaco de concorrência da conta. O token é a representação
/// Base64 do <c>rowversion</c> de <c>UsuarioAplicacao</c>, distinto do security
/// stamp de autenticação e nunca reutilizado como credencial.
/// </summary>
public static class TokenConcorrencia
{
    public static string Serializar(byte[] rowVersion) =>
        rowVersion.Length == 0 ? string.Empty : Convert.ToBase64String(rowVersion);

    public static bool TentarDesserializar(string? token, out byte[] rowVersion)
    {
        rowVersion = [];

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            rowVersion = Convert.FromBase64String(token);
            return rowVersion.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
