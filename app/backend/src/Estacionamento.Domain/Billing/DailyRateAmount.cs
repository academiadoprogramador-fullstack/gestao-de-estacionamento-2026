namespace Estacionamento.Domain.Billing;

public sealed record DailyRateAmount
{
    private DailyRateAmount(decimal value) => Value = value;

    public decimal Value { get; }

    public static DailyRateAmount Create(decimal value)
    {
        if (value <= 0 || decimal.Round(value, 2) != value)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Daily rate must be positive and have at most two decimal places.");
        }

        return new DailyRateAmount(value);
    }
}
