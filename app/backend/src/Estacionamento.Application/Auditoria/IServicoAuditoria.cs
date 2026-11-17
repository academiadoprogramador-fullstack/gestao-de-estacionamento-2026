namespace Estacionamento.Application.Auditoria;

/// <summary>
/// Evento de auditoria de autenticação. Não contém senha, token, refresh nem
/// dados pessoais; o ator é sempre uma referência opaca.
/// </summary>
public sealed record EventoAuditoriaAutenticacao(
    string Tipo,
    string? AtorOpaco,
    string? AlvoOpaco,
    string? Correlacao,
    string? Detalhe,
    DateTimeOffset OcorridoEmUtc);

/// <summary>
/// Porta de gravação de auditoria append-only de autenticação.
/// </summary>
public interface IServicoAuditoria
{
    /// <summary>
    /// Adiciona o evento ao contexto atual sem salvar, permitindo que a
    /// auditoria participe da mesma transação da operação de autenticação.
    /// </summary>
    void Adicionar(EventoAuditoriaAutenticacao evento);

    /// <summary>
    /// Adiciona e salva o evento imediatamente.
    /// </summary>
    Task RegistrarAsync(EventoAuditoriaAutenticacao evento, CancellationToken cancellationToken = default);
}
