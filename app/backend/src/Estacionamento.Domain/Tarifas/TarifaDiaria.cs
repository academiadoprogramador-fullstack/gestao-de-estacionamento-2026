namespace Estacionamento.Domain.Tarifas;

/// <summary>
/// Versão imutável de diária publicada pelo administrador. Publicações
/// posteriores não alteram registros anteriores.
/// </summary>
public sealed class TarifaDiaria
{
    public Guid Id { get; set; }
    public int Versao { get; set; }
    public decimal Valor { get; set; }
    public Guid PublicadoPorId { get; set; }
    public DateTimeOffset PublicadoEmUtc { get; set; }
}

/// <summary>
/// Configuração singleton (<c>Id = 1</c>) que aponta para a versão corrente.
/// Ausência de versão significa "sem tarifa publicada".
/// </summary>
public sealed class ConfiguracaoTarifa
{
    public const int IdSingleton = 1;

    public int Id { get; set; } = IdSingleton;
    public Guid? VersaoCorrenteId { get; set; }

    /// <summary>Versão de concorrência usada nas publicações.</summary>
    public byte[] RowVersion { get; set; } = [];
}
