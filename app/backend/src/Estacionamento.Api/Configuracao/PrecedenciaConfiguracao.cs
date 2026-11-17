using Microsoft.Extensions.Configuration.Json;

namespace Estacionamento.Api.Configuracao;

/// <summary>
/// Precedência de configuração do .NET: appsettings.json →
/// appsettings.{Environment}.json → User Secrets (somente Development) →
/// variáveis de ambiente → argumentos. Testes podem desabilitar o carregamento
/// de User Secrets pessoais com ESTACIONAMENTO_DESABILITAR_USER_SECRETS=1.
/// </summary>
public static class PrecedenciaConfiguracao
{
    public const string VariavelDesabilitarUserSecrets = "ESTACIONAMENTO_DESABILITAR_USER_SECRETS";

    public static bool DeveCarregarUserSecrets(string ambiente, string? userSecretsId) =>
        string.Equals(ambiente, Environments.Development, StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(userSecretsId);

    /// <summary>
    /// Remove fontes de User Secrets quando o ambiente não é Development ou
    /// quando os testes solicitam explicitamente o isolamento da configuração
    /// pessoal do desenvolvedor.
    /// </summary>
    public static void AplicarRestricaoDeUserSecrets(
        IConfigurationManager configuracao,
        string ambiente,
        string? userSecretsId,
        Func<string, string?>? lerVariavelAmbiente = null)
    {
        var leitor = lerVariavelAmbiente ?? Environment.GetEnvironmentVariable;
        var desabilitado = string.Equals(
            leitor(VariavelDesabilitarUserSecrets),
            "1",
            StringComparison.Ordinal);

        if (DeveCarregarUserSecrets(ambiente, userSecretsId) && !desabilitado)
        {
            return;
        }

        var fontesDeSecrets = configuracao.Sources
            .OfType<JsonConfigurationSource>()
            .Where(fonte => fonte.Path is not null &&
                            fonte.Path.EndsWith("secrets.json", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var fonte in fontesDeSecrets)
        {
            configuracao.Sources.Remove(fonte);
        }
    }
}
