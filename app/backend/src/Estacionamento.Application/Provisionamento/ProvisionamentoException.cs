namespace Estacionamento.Application.Provisionamento;

/// <summary>
/// Falha controlada do provisionamento. A mensagem contém apenas código e
/// perfil, nunca e-mail, senha ou identificador de conta.
/// </summary>
public sealed class ProvisionamentoException : Exception
{
    public ProvisionamentoException(string codigo, string perfil)
        : base($"Falha de provisionamento ({codigo}) para o perfil {perfil}.")
    {
        Codigo = codigo;
        Perfil = perfil;
    }

    public string Codigo { get; }

    public string Perfil { get; }
}
