using Estacionamento.Application.Comum;

namespace Estacionamento.Application.Vagas;

/// <summary>Cadastro administrativo de vaga.</summary>
public sealed record RequisicaoCadastrarVaga(string Codigo);

/// <summary>Vaga com situação derivada da ocupação aberta.</summary>
public sealed record VagaResumo(
    Guid Id,
    string Codigo,
    string Situacao,
    string TokenConcorrencia);

/// <summary>Filtro informativo da consulta de vagas.</summary>
public sealed record FiltroVagas(string? Codigo, string? Situacao);

/// <summary>Cadastro e consulta paginada de vagas.</summary>
public interface IServicoVagas
{
    Task<VagaResumo> CadastrarAsync(
        AtorAtual ator,
        RequisicaoCadastrarVaga requisicao,
        string? correlacao,
        CancellationToken cancellationToken = default);

    Task<Pagina<VagaResumo>> ConsultarAsync(
        AtorAtual ator,
        FiltroVagas filtro,
        int pagina,
        int tamanho,
        CancellationToken cancellationToken = default);
}
