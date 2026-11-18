using Estacionamento.Application.Comum;

namespace Estacionamento.Application.Funcionarios;

/// <summary>Criação de funcionário pelo administrador, sem autorregistro.</summary>
public sealed record RequisicaoCriarFuncionario(
    string Nome,
    string Email,
    string SenhaInicial,
    IReadOnlyList<string> Perfis);

/// <summary>
/// Alteração de perfis e estado com o token opaco de concorrência da conta.
/// </summary>
public sealed record RequisicaoAlterarAcesso(
    bool Ativo,
    IReadOnlyList<string> Perfis,
    string TokenConcorrencia);

/// <summary>Projeção de funcionário sem senha, hash, token ou segredo.</summary>
public sealed record FuncionarioResumo(
    Guid Id,
    string Nome,
    string Email,
    bool Ativo,
    IReadOnlyList<string> Perfis,
    string TokenConcorrencia);

/// <summary>Casos de uso administrativos de funcionários e acessos.</summary>
public interface IServicoFuncionarios
{
    Task<FuncionarioResumo> CriarAsync(
        AtorAtual ator,
        RequisicaoCriarFuncionario requisicao,
        string? correlacao,
        CancellationToken cancellationToken = default);

    Task<Pagina<FuncionarioResumo>> ListarAsync(
        AtorAtual ator,
        string? status,
        int pagina,
        int tamanho,
        CancellationToken cancellationToken = default);

    Task<FuncionarioResumo> AlterarAcessoAsync(
        AtorAtual ator,
        Guid funcionarioId,
        RequisicaoAlterarAcesso requisicao,
        string? correlacao,
        CancellationToken cancellationToken = default);
}
