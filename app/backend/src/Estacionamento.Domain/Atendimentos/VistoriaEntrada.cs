namespace Estacionamento.Domain.Atendimentos;

/// <summary>
/// Vistoria textual de entrada, única por atendimento e imutável após a
/// confirmação. O texto é sempre persistido cifrado.
/// </summary>
public sealed class VistoriaEntrada
{
    public Guid AtendimentoId { get; set; }

    /// <summary>Texto da vistoria em envelope AES-256-GCM.</summary>
    public byte[] TextoCifrado { get; set; } = [];

    public Guid AtorId { get; set; }
    public DateTimeOffset CriadoEmUtc { get; set; }
}
