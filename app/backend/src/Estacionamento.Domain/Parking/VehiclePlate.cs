namespace Estacionamento.Domain.Parking;

public sealed record VehiclePlate
{
    public const int MaximumLength = 16;

    private VehiclePlate(string value) => Value = value;

    public string Value { get; }

    public static VehiclePlate Create(string? value)
    {
        var normalized = new string((value ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());

        if (normalized.Length is 0 or > MaximumLength)
        {
            throw new ArgumentException($"Plate must contain 1 to {MaximumLength} alphanumeric characters.", nameof(value));
        }

        return new VehiclePlate(normalized);
    }
}
