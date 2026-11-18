namespace Estacionamento.Domain.Tarifas;

/// <summary>
/// Valor monetário de uma diária em <c>decimal(18,2)</c>. Rejeita zero,
/// negativos, escala excedente e valores acima do limite representável, sem
/// arredondamento silencioso.
/// </summary>
public readonly record struct ValorDiaria(decimal Valor)
{
    /// <summary>Maior valor representável em decimal(18,2).</summary>
    public const decimal Maximo = 9_999_999_999_999_999.99m;

    public static bool TentarCriar(decimal valor, out ValorDiaria resultado)
    {
        resultado = default;

        if (valor <= 0 || valor > Maximo)
        {
            return false;
        }

        if (decimal.Round(valor, 2) != valor)
        {
            return false;
        }

        resultado = new ValorDiaria(valor);
        return true;
    }
}
