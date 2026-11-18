using Estacionamento.Application.Seguranca;

namespace Estacionamento.Infrastructure.Seguranca;

/// <summary>
/// Contexto fixo do processo usado no vínculo autenticado dos campos. O nome do
/// ambiente vem do host e nunca do cliente.
/// </summary>
public sealed class ContextoAplicacao : IContextoAplicacao
{
    public ContextoAplicacao(string nomeAmbiente)
    {
        NomeAmbiente = nomeAmbiente;
    }

    public string NomeAmbiente { get; }

    public string NomeAplicacao => "Estacionamento";
}
