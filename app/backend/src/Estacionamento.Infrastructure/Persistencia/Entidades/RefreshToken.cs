namespace Estacionamento.Infrastructure.Persistencia.Entidades;

/// <summary>
/// Refresh opaco. O SQL guarda somente o hash SHA-256; o valor puro existe
/// apenas em memória do cliente e trafega no corpo JSON sobre HTTPS.
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; set; }
    public Guid SessaoId { get; set; }
    public string HashSha256 { get; set; } = string.Empty;
    public DateTimeOffset CriadoEmUtc { get; set; }
    public DateTimeOffset ExpiraEmUtc { get; set; }
    public DateTimeOffset? ConsumidoEmUtc { get; set; }
    public DateTimeOffset? RevogadoEmUtc { get; set; }
    public Guid? SubstituidoPorId { get; set; }

    /// <summary>Versão de concorrência do registro de refresh.</summary>
    public byte[] RowVersion { get; set; } = [];
}
