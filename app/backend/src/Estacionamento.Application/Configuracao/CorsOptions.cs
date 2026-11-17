using Microsoft.Extensions.Options;

namespace Estacionamento.Application.Configuracao;

/// <summary>
/// Origens exatas autorizadas para chamadas diretas da SPA à API.
/// </summary>
public sealed class CorsOptions
{
    public const string Secao = "Cors";

    public string[] OrigensPermitidas { get; set; } = [];
}

/// <summary>
/// Rejeita lista vazia, valores não absolutos, HTTP sem TLS, curingas e
/// duplicidades. CORS complementa, mas não substitui, autenticação.
/// </summary>
public sealed class CorsOptionsValidator : IValidateOptions<CorsOptions>
{
    public ValidateOptionsResult Validate(string? name, CorsOptions options)
    {
        var erros = new List<string>();

        if (options.OrigensPermitidas.Length == 0)
        {
            erros.Add("Cors.OrigensPermitidas deve conter ao menos uma origem exata.");
        }

        if (options.OrigensPermitidas.Any(origem => origem.Contains('*', StringComparison.Ordinal)))
        {
            erros.Add("Cors.OrigensPermitidas não aceita curinga.");
        }

        if (options.OrigensPermitidas
                .Where(origem => !string.IsNullOrWhiteSpace(origem))
                .GroupBy(origem => origem.TrimEnd('/'), StringComparer.OrdinalIgnoreCase)
                .Any(grupo => grupo.Count() > 1))
        {
            erros.Add("Cors.OrigensPermitidas contém origem duplicada.");
        }

        foreach (var origem in options.OrigensPermitidas)
        {
            if (!Uri.TryCreate(origem, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                !string.IsNullOrEmpty(uri.AbsolutePath.Trim('/')) ||
                !string.IsNullOrEmpty(uri.Query))
            {
                erros.Add("Cors.OrigensPermitidas aceita somente origens HTTPS absolutas, sem caminho ou query.");
                break;
            }
        }

        return erros.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(erros);
    }
}
