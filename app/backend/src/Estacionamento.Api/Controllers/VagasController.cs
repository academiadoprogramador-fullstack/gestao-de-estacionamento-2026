using Estacionamento.Api.Autenticacao;
using Estacionamento.Api.Infra;
using Estacionamento.Application.Vagas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Estacionamento.Api.Controllers;

/// <summary>
/// Cadastro administrativo e consulta compartilhada de vagas. A situação é
/// derivada no servidor e a consulta nunca reserva uma vaga.
/// </summary>
[ApiController]
[Route("api/v1/spaces")]
public sealed class VagasController : ControllerBase
{
    private readonly IServicoVagas _vagas;

    public VagasController(IServicoVagas vagas)
    {
        _vagas = vagas;
    }

    [HttpGet]
    [Authorize(Policy = PoliticasAutorizacao.Funcionario)]
    public async Task<IActionResult> Consultar(
        [FromQuery] string? code,
        [FromQuery] string? status,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var (pagina, tamanho) = ParametrosHttp.LerPaginacao(page, pageSize);

        var resultado = await _vagas.ConsultarAsync(
            AtorHttp.Obter(User),
            new FiltroVagas(code, status),
            pagina,
            tamanho,
            cancellationToken);

        return Ok(resultado);
    }

    [HttpPost]
    [Authorize(Policy = PoliticasAutorizacao.Administrador)]
    public async Task<IActionResult> Cadastrar(
        [FromBody] CadastrarVagaRequisicao requisicao,
        CancellationToken cancellationToken)
    {
        var resultado = await _vagas.CadastrarAsync(
            AtorHttp.Obter(User),
            new RequisicaoCadastrarVaga(requisicao.Codigo ?? string.Empty),
            CorrelacaoHttp.Obter(HttpContext),
            cancellationToken);

        return Created($"/api/v1/spaces/{resultado.Id}", resultado);
    }
}

/// <summary>Código informado no cadastro de vaga.</summary>
public sealed record CadastrarVagaRequisicao(string? Codigo);
