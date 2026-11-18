namespace Estacionamento.Domain.Vagas;

/// <summary>
/// Vaga da unidade. Não há edição nem exclusão nesta fase; a situação livre ou
/// ocupada é derivada da ocupação aberta, sem coluna redundante.
/// </summary>
public sealed class Vaga
{
    public Guid Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string CodigoNormalizado { get; set; } = string.Empty;
    public Guid CriadoPorId { get; set; }
    public DateTimeOffset CriadoEmUtc { get; set; }

    /// <summary>Versão de concorrência devolvida como token opaco na consulta.</summary>
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>Situações informativas devolvidas pela consulta de vagas.</summary>
public static class SituacaoVaga
{
    public const string Livre = "livre";
    public const string Ocupada = "ocupada";
}
