namespace Estacionamento.Domain.Idempotencia;

/// <summary>
/// Resultado mínimo de um comando idempotente. A chave é única por ator,
/// operação e valor; o conteúdo é comparado por SHA-256 de uma representação
/// canônica versionada, sem corpo da requisição persistido.
/// </summary>
public sealed class ResultadoIdempotente
{
    public const string OperacaoRegistrarEntrada = "attendance.check-in";

    public Guid Id { get; set; }
    public Guid AtorId { get; set; }
    public string Operacao { get; set; } = string.Empty;
    public Guid Chave { get; set; }
    public int VersaoCanonicalizacao { get; set; }
    public byte[] HashSha256 { get; set; } = [];
    public DateTimeOffset CriadoEmUtc { get; set; }

    // Resultado mínimo preenchido na mesma transação do comando. Nulo enquanto
    // a reserva da chave ainda não foi confirmada.
    public string? IdentificadorPublico { get; set; }
    public Guid? AtendimentoId { get; set; }
    public Guid? VagaId { get; set; }
    public string? Estado { get; set; }
    public DateTimeOffset? EntradaUtc { get; set; }
    public string? Location { get; set; }
}
