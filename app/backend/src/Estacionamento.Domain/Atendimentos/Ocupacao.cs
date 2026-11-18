namespace Estacionamento.Domain.Atendimentos;

/// <summary>
/// Ocupação de vaga. Enquanto aberta (<see cref="FimUtc"/> nulo), o índice
/// único filtrado garante uma única ocupação por vaga e por atendimento.
/// </summary>
public sealed class Ocupacao
{
    public Guid Id { get; set; }
    public Guid VagaId { get; set; }
    public Guid AtendimentoId { get; set; }
    public DateTimeOffset InicioUtc { get; set; }
    public DateTimeOffset? FimUtc { get; set; }
    public Guid ResponsavelId { get; set; }
}
