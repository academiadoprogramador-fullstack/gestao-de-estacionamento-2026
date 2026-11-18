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
