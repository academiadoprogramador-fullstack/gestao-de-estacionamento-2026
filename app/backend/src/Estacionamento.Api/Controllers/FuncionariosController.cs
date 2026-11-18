using Estacionamento.Api.Autenticacao;
using Estacionamento.Api.Infra;
using Estacionamento.Application.Funcionarios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Estacionamento.Api.Controllers;

/// <summary>
/// Administração de funcionários e acessos. Todas as rotas exigem o perfil
/// Administrador e nunca retornam senha, hash ou token de autenticação.
/// </summary>
[ApiController]
[Route("api/v1/employees")]
[Authorize(Policy = PoliticasAutorizacao.Administrador)]
public sealed class FuncionariosController : ControllerBase
{
    private readonly IServicoFuncionarios _funcionarios;

    public FuncionariosController(IServicoFuncionarios funcionarios)
    {
        _funcionarios = funcionarios;
    }

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        var (pagina, tamanho) = ParametrosHttp.LerPaginacao(page, pageSize);

        var resultado = await _funcionarios.ListarAsync(
            AtorHttp.Obter(User),
            status,
            pagina,
            tamanho,
            cancellationToken);

        return Ok(resultado);
    }

    [HttpPost]
    public async Task<IActionResult> Criar(
        [FromBody] CriarFuncionarioRequisicao requisicao,
        CancellationToken cancellationToken)
    {
        var resultado = await _funcionarios.CriarAsync(
            AtorHttp.Obter(User),
            new RequisicaoCriarFuncionario(
                requisicao.Nome,
                requisicao.Email,
                requisicao.SenhaInicial,
                requisicao.Perfis ?? []),
            CorrelacaoHttp.Obter(HttpContext),
            cancellationToken);

        return Created($"/api/v1/employees/{resultado.Id}", resultado);
    }

    [HttpPut("{id:guid}/access")]
    public async Task<IActionResult> AlterarAcesso(
        Guid id,
        [FromBody] AlterarAcessoRequisicao requisicao,
        CancellationToken cancellationToken)
    {
        var resultado = await _funcionarios.AlterarAcessoAsync(
            AtorHttp.Obter(User),
            id,
            new RequisicaoAlterarAcesso(
                requisicao.Ativo,
                requisicao.Perfis ?? [],
                requisicao.TokenConcorrencia ?? string.Empty),
            CorrelacaoHttp.Obter(HttpContext),
            cancellationToken);

        return Ok(resultado);
    }
}

/// <summary>Dados de criação de funcionário sem qualquer campo do servidor.</summary>
public sealed record CriarFuncionarioRequisicao(
    string Nome,
    string Email,
    string SenhaInicial,
    IReadOnlyList<string>? Perfis);

/// <summary>Estado, perfis e token de concorrência da conta.</summary>
public sealed record AlterarAcessoRequisicao(
    bool Ativo,
    IReadOnlyList<string>? Perfis,
    string? TokenConcorrencia);
