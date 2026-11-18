using Estacionamento.Api.Autenticacao;
using Estacionamento.Api.Infra;
using Estacionamento.Application.Tarifas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Estacionamento.Api.Controllers;

/// <summary>
/// Publicação, consulta corrente e histórico de tarifas. Todas as rotas são
/// exclusivas do Administrador; não existe tarifa comercial padrão.
/// </summary>
[ApiController]
[Route("api/v1/daily-rates")]
[Authorize(Policy = PoliticasAutorizacao.Administrador)]
public sealed class TarifasController : ControllerBase
{
    private readonly IServicoTarifas _tarifas;

    public TarifasController(IServicoTarifas tarifas)
    {
        _tarifas = tarifas;
    }

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var (pagina, tamanho) = ParametrosHttp.LerPaginacao(page, pageSize);

        var resultado = await _tarifas.ListarAsync(
            AtorHttp.Obter(User),
            pagina,
            tamanho,
            cancellationToken);

        return Ok(resultado);
    }

    [HttpGet("current")]
    public async Task<IActionResult> Corrente(CancellationToken cancellationToken) =>
        Ok(await _tarifas.ObterCorrenteAsync(cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Publicar(
        [FromBody] PublicarTarifaRequisicao requisicao,
        CancellationToken cancellationToken)
    {
        var resultado = await _tarifas.PublicarAsync(
            AtorHttp.Obter(User),
            new RequisicaoPublicarTarifa(requisicao.Valor, requisicao.VersaoBaseEsperada),
            CorrelacaoHttp.Obter(HttpContext),
            cancellationToken);

        return Created("/api/v1/daily-rates", resultado);
    }
}

/// <summary>
/// Valor positivo e versão-base corrente (nula quando ainda não há tarifa).
/// </summary>
public sealed record PublicarTarifaRequisicao(decimal Valor, int? VersaoBaseEsperada);
