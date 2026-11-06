namespace Estacionamento.Api.Configuration;

public static class RuntimeConfiguration
{
    public static void ValidateRequired(IConfiguration configuration)
    {
        Require(configuration, "ConnectionStrings:Identity");
        Require(configuration, "DataProtection:KeyRingPath");

        var originValue = Require(configuration, "Security:SpaOrigin");
        if (!Uri.TryCreate(originValue, UriKind.Absolute, out var origin)
            || origin.Scheme != Uri.UriSchemeHttps
            || origin.AbsolutePath != "/"
            || !string.IsNullOrEmpty(origin.Query)
            || !string.IsNullOrEmpty(origin.Fragment))
        {
            throw new InvalidOperationException(
                "Security:SpaOrigin must be an HTTPS origin without a path, query, or fragment.");
        }
    }

    private static string Require(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Required configuration '{key}' is missing.");
        }

        return value;
    }
}
