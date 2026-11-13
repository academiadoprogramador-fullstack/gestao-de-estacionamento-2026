using Estacionamento.Application.Idempotency;

namespace Estacionamento.Api.Security;

public sealed class ConfiguredCheckInContentHmac(IConfiguration configuration) : ICheckInContentHmac
{
    private readonly CheckInContentHmac implementation = Create(configuration);

    public int ActiveKeyVersion => implementation.ActiveKeyVersion;

    public CheckInContentDigest Compute(CanonicalCheckInContent content) => implementation.Compute(content);

    public bool Matches(CanonicalCheckInContent content, int keyVersion, ReadOnlySpan<byte> expectedHmac) =>
        implementation.Matches(content, keyVersion, expectedHmac);

    private static CheckInContentHmac Create(IConfiguration configuration)
    {
        var activeValue = configuration["Idempotency:Hmac:ActiveKeyVersion"];
        if (!int.TryParse(activeValue, out var activeVersion) || activeVersion <= 0)
        {
            throw new InvalidOperationException("Idempotency:Hmac:ActiveKeyVersion must be a positive integer.");
        }

        var keys = new Dictionary<int, byte[]>();
        foreach (var section in configuration.GetSection("Idempotency:Hmac:Keys").GetChildren())
        {
            if (!int.TryParse(section.Key, out var version) || version <= 0
                || string.IsNullOrWhiteSpace(section.Value))
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

            keys.Add(version, key);
        }

        if (!keys.ContainsKey(activeVersion))
        {
            throw new InvalidOperationException("The active idempotency HMAC key version is not configured.");
        }

        return new CheckInContentHmac(activeVersion, keys);
    }
}
