namespace Estacionamento.Infrastructure.Persistencia.Entidades;

/// <summary>
/// Sessão persistida emitida no login. A revogação é verificada no SQL em cada
/// requisição protegida, sem cache permissivo entre requisições.
/// </summary>
public sealed class SessaoAutenticacao
{
    public Guid Id { get; set; }
    public Guid UsuarioId { get; set; }
    public long VersaoAcesso { get; set; }
    public DateTimeOffset CriadaEmUtc { get; set; }
    public DateTimeOffset ExpiraEmUtc { get; set; }
    public DateTimeOffset? RevogadaEmUtc { get; set; }
    public string? CausaRevogacao { get; set; }

    /// <summary>Versão de concorrência usada em revogações e renovações.</summary>
    public byte[] RowVersion { get; set; } = [];
}
