namespace Estacionamento.Domain.Entrega;

/// <summary>
/// Vistoria textual de saída, única por atendimento e imutável após a
/// confirmação. O texto é sempre persistido cifrado e o registro referencia o
/// pedido de retirada da visita.
/// </summary>
public sealed class VistoriaSaida
{
    public Guid Id { get; set; }
    public Guid AtendimentoId { get; set; }
    public Guid PedidoId { get; set; }

    /// <summary>Texto da vistoria em envelope AES-256-GCM.</summary>
    public byte[] TextoCifrado { get; set; } = [];

    public Guid AtorId { get; set; }
    public DateTimeOffset CriadoEmUtc { get; set; }
}

/// <summary>
/// Entrega confirmada de um atendimento. Append-only: registra o ator, o
/// horário do servidor e os vínculos imutáveis da visita (vistoria, ocupação e
/// vaga). A disponibilidade corrente não é derivada deste registro.
/// </summary>
public sealed class EntregaVeiculo
{
    public Guid Id { get; set; }
    public Guid AtendimentoId { get; set; }
    public Guid PedidoId { get; set; }
    public Guid VistoriaSaidaId { get; set; }
    public Guid OcupacaoId { get; set; }
    public Guid VagaId { get; set; }
    public Guid AtorId { get; set; }
    public DateTimeOffset EntregueEmUtc { get; set; }
}

/// <summary>
/// Consumo de uma autorização de exceção na entrega. Um registro por
/// entrega/tipo, referenciando a concessão e a revisão utilizadas, sem copiar a
/// justificativa. Nunca é alterado nem excluído.
/// </summary>
public sealed class ConsumoAutorizacao
{
    public Guid Id { get; set; }
    public Guid EntregaId { get; set; }
    public Guid ConcessaoId { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public int RevisaoReferencia { get; set; }
    public DateTimeOffset ConsumidaEmUtc { get; set; }
}
