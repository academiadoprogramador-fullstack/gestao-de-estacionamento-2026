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

        ValidateIdempotencyHmac(configuration);
    }

    private static void ValidateIdempotencyHmac(IConfiguration configuration)
    {
        var activeValue = Require(configuration, "Idempotency:Hmac:ActiveKeyVersion");
        if (!int.TryParse(activeValue, out var activeVersion) || activeVersion <= 0)
        {
            throw new InvalidOperationException("Idempotency:Hmac:ActiveKeyVersion must be a positive integer.");
        }

        var keys = configuration.GetSection("Idempotency:Hmac:Keys").GetChildren().ToArray();
        if (keys.Length == 0)
        {
            throw new InvalidOperationException("At least one versioned idempotency HMAC key is required.");
        }

        var activeConfigured = false;
        foreach (var section in keys)
        {
            if (!int.TryParse(section.Key, out var version) || version <= 0 || string.IsNullOrWhiteSpace(section.Value))
            {
                throw new InvalidOperationException("Idempotency HMAC keys must use positive numeric versions and base64 values.");
            }

            byte[] key;
            try
            {
                key = Convert.FromBase64String(section.Value);
            }
            catch (FormatException exception)
            {
                throw new InvalidOperationException($"Idempotency HMAC key version {version} is not valid base64.", exception);
            }

            if (key.Length < 32)
            {
                throw new InvalidOperationException($"Idempotency HMAC key version {version} must contain at least 32 bytes.");
            }

            activeConfigured |= version == activeVersion;
        }

        if (!activeConfigured)
        {
            throw new InvalidOperationException("The active idempotency HMAC key version is not configured.");
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
