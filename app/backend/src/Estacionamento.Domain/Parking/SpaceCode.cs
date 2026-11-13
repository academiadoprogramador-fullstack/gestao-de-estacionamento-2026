namespace Estacionamento.Domain.Parking;

public sealed record SpaceCode
{
    public const int MaximumLength = 32;

    private SpaceCode(string value) => Value = value;

    public string Value { get; }

    public static SpaceCode Create(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > MaximumLength)
        {
            throw new ArgumentException($"Space code must contain 1 to {MaximumLength} characters.", nameof(value));
        }

        return new SpaceCode(normalized);
    }
}
