namespace Estacionamento.Domain.Atendimentos;

/// <summary>
/// Atendimento ativo com snapshot imutável de tarifa. Horário, operador e
/// identificador público são sempre do servidor.
/// </summary>
public sealed class Atendimento
{
    public Guid Id { get; set; }

    /// <summary>Identificador público único no formato <c>AT-ano-sequência</c>.</summary>
    public string? IdentificadorPublico { get; set; }

    public string Placa { get; set; } = string.Empty;
    public string Modelo { get; set; } = string.Empty;
    public string Titular { get; set; } = string.Empty;

    /// <summary>Documento do titular cifrado em envelope versionado.</summary>
    public byte[] DocumentoTitularCifrado { get; set; } = [];

    public Guid TarifaVersaoId { get; set; }
    public decimal TarifaValorSnapshot { get; set; }
    public DateTimeOffset EntradaUtc { get; set; }
    public Guid OperadorId { get; set; }
    public string Estado { get; set; } = EstadoAtendimento.Ativo;

    /// <summary>Versão de concorrência do atendimento.</summary>
    public byte[] RowVersion { get; set; } = [];
}
