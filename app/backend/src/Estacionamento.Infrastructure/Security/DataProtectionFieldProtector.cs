using Estacionamento.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;

namespace Estacionamento.Infrastructure.Security;

public sealed class DataProtectionFieldProtector(IDataProtectionProvider provider) : IFieldProtector
{
    private const string EnvelopePrefix = "dp";
    private const int CurrentFormatVersion = 1;

    public string Protect(string value, Guid aggregateId, string field, int version)
    {
        ArgumentNullException.ThrowIfNull(value);
        var protector = CreateFieldProtector(aggregateId, field, version);
        return $"{EnvelopePrefix}{CurrentFormatVersion}:{version}:{protector.Protect(value)}";
    }

    public string Unprotect(string protectedValue, Guid aggregateId, string field, int version)
    {
        ArgumentNullException.ThrowIfNull(protectedValue);
        var parts = protectedValue.Split(':', 3, StringSplitOptions.None);
        if (parts.Length != 3
            || !string.Equals(parts[0], $"{EnvelopePrefix}{CurrentFormatVersion}", StringComparison.Ordinal)
            || !int.TryParse(parts[1], out var envelopeVersion)
            || envelopeVersion != version
            || version <= 0)
        {
            throw new FormatException("The protected field envelope is invalid or uses an unsupported version.");
        }

        return CreateFieldProtector(aggregateId, field, version).Unprotect(parts[2]);
    }

    private IDataProtector CreateFieldProtector(Guid aggregateId, string field, int version)
    {
        if (aggregateId == Guid.Empty || version <= 0)
        {
            throw new ArgumentException("A non-empty aggregate identifier and positive field version are required.");
        }

        if (field is not ("document" or "entry-inspection"))
        {
            throw new ArgumentException("Only approved sensitive fields may be protected.", nameof(field));
        }

        return provider.CreateProtector("GestaoEstacionamento.FieldProtection")
            .CreateProtector($"format:{CurrentFormatVersion}")
            .CreateProtector($"aggregate:{aggregateId:N}")
            .CreateProtector($"field:{field}")
            .CreateProtector($"field-version:{version}");
    }
}
