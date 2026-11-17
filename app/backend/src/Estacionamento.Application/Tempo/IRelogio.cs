namespace Estacionamento.Application.Tempo;

/// <summary>
/// Fonte de tempo do servidor. Toda auditoria e expiração usa UTC obtido desta
/// abstração, evitando depender do relógio do cliente ou de chamadas estáticas
/// em testes.
/// </summary>
public interface IRelogio
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>
/// Implementação de produção baseada no relógio do sistema.
/// </summary>
public sealed class RelogioSistema : IRelogio
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
