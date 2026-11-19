namespace Estacionamento.Application.Seguranca;

/// <summary>
/// Cifragem autenticada de campos sensíveis por propósito e registro. A
/// implementação usa AES-256-GCM com chave versionada e AAD reconstruído no
/// servidor; falhas resultam em erro controlado sem fallback em texto puro.
/// </summary>
public interface ICriptografiaCampos
{
    /// <summary>Cifra o texto para o propósito e atendimento informados.</summary>
    byte[] Cifrar(string proposito, Guid atendimentoId, string texto);

    /// <summary>Decifra o envelope somente com o propósito e registro corretos.</summary>
    string Decifrar(string proposito, Guid atendimentoId, byte[] envelope);

    /// <summary>
    /// Cifra o texto vinculando o envelope ao atendimento e ao identificador
    /// opaco do registro. Impede a troca de justificativas entre lançamentos e
    /// concessões do mesmo atendimento.
    /// </summary>
    byte[] CifrarComRegistro(string proposito, Guid atendimentoId, Guid registroId, string texto);

    /// <summary>
    /// Decifra somente com o propósito, o atendimento e o registro corretos.
    /// </summary>
    string DecifrarComRegistro(
        string proposito,
        Guid atendimentoId,
        Guid registroId,
        byte[] envelope);
}

/// <summary>
/// Contexto de execução usado no vínculo autenticado (AAD) e em mensagens. O
/// nome do ambiente nunca é fornecido pelo cliente.
/// </summary>
public interface IContextoAplicacao
{
    string NomeAmbiente { get; }

    string NomeAplicacao { get; }
}
