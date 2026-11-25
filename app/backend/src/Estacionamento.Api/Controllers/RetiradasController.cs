using Estacionamento.Api.Autenticacao;
using Estacionamento.Api.Infra;
using Estacionamento.Application.Caixa;
using Estacionamento.Application.Entrega;
using Estacionamento.Application.Retiradas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Estacionamento.Api.Controllers;

/// <summary>
/// Fila, pedido, acerto, ajustes, autorizações, prontidão e entrega da
/// retirada. A vistoria de saída vive no caminho do atendimento; consumo de
/// autorização e liberação de vaga só acontecem na confirmação da entrega.
/// </summary>
[ApiController]
[Route("api/v1/withdrawals")]
public sealed class RetiradasController : ControllerBase
{
    private readonly IServicoRetiradas _retiradas;
    private readonly IServicoConsultaRetiradas _consulta;
    private readonly IServicoCaixa _caixa;
    private readonly IServicoAutorizacoes _autorizacoes;
    private readonly IServicoEntrega _entrega;

    public RetiradasController(
        IServicoRetiradas retiradas,
        IServicoConsultaRetiradas consulta,
        IServicoCaixa caixa,
        IServicoAutorizacoes autorizacoes,
        IServicoEntrega entrega)
    {
        _retiradas = retiradas;
        _consulta = consulta;
        _caixa = caixa;
        _autorizacoes = autorizacoes;
        _entrega = entrega;
    }

    [HttpGet]
    [Authorize(Policy = PoliticasAutorizacao.Funcionario)]
    public async Task<IActionResult> Listar(
        [FromQuery] string? state,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var (pagina, tamanho) = ParametrosHttp.LerPaginacao(page, pageSize);

        return Ok(await _consulta.ListarAsync(
            AtorHttp.Obter(User),
            state,
            pagina,
            tamanho,
            cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PoliticasAutorizacao.Funcionario)]
    public async Task<IActionResult> Detalhe(Guid id, CancellationToken cancellationToken) =>
        Ok(await _consulta.ObterAsync(AtorHttp.Obter(User), id, cancellationToken));

    [HttpPost("{id:guid}/search")]
    [Authorize(Policy = PoliticasAutorizacao.Funcionario)]
    public async Task<IActionResult> IniciarBusca(
        Guid id,
        [FromBody] IniciarBuscaRequisicao requisicao,
        CancellationToken cancellationToken) =>
        Ok(await _retiradas.IniciarBuscaAsync(
            AtorHttp.Obter(User),
            id,
            requisicao.VersaoEsperada ?? [],
            CorrelacaoHttp.Obter(HttpContext),
            cancellationToken));

    [HttpGet("{id:guid}/cashier")]
    [Authorize(Policy = PoliticasAutorizacao.Operador)]
    public async Task<IActionResult> Caixa(Guid id, CancellationToken cancellationToken) =>
        Ok(await _consulta.ObterAsync(AtorHttp.Obter(User), id, cancellationToken));

    [HttpPost("{id:guid}/payments")]
    [Authorize(Policy = PoliticasAutorizacao.Operador)]
    public async Task<IActionResult> RegistrarPagamento(
        Guid id,
        [FromBody] RequisicaoPagamento requisicao,
        CancellationToken cancellationToken)
    {
        var resultado = await _caixa.RegistrarPagamentoAsync(
            AtorHttp.Obter(User),
            id,
            requisicao,
            ParametrosHttp.LerChaveIdempotencia(Request),
            CorrelacaoHttp.Obter(HttpContext),
            cancellationToken);

        return Created(resultado.Location, resultado);
    }

    [HttpPost("{id:guid}/discounts")]
    [Authorize(Policy = PoliticasAutorizacao.Administrador)]
    public async Task<IActionResult> RegistrarDesconto(
        Guid id,
        [FromBody] RequisicaoDesconto requisicao,
        CancellationToken cancellationToken)
    {
        var resultado = await _caixa.RegistrarDescontoAsync(
            AtorHttp.Obter(User),
            id,
            requisicao,
            ParametrosHttp.LerChaveIdempotencia(Request),
            CorrelacaoHttp.Obter(HttpContext),
            cancellationToken);

        return Created(resultado.Location, resultado);
    }

    [HttpPost("{id:guid}/refunds")]
    [Authorize(Policy = PoliticasAutorizacao.Administrador)]
    public async Task<IActionResult> RegistrarEstorno(
        Guid id,
        [FromBody] RequisicaoEstorno requisicao,
        CancellationToken cancellationToken)
    {
        var resultado = await _caixa.RegistrarEstornoAsync(
            AtorHttp.Obter(User),
            id,
            requisicao,
            ParametrosHttp.LerChaveIdempotencia(Request),
            CorrelacaoHttp.Obter(HttpContext),
            cancellationToken);

        return Created(resultado.Location, resultado);
    }

    [HttpGet("{id:guid}/launches/{launchId:guid}/justification")]
    [Authorize(Policy = PoliticasAutorizacao.Administrador)]
    public async Task<IActionResult> JustificativaLancamento(
        Guid id,
        Guid launchId,
        CancellationToken cancellationToken) =>
        Ok(await _caixa.LerJustificativaLancamentoAsync(
            AtorHttp.Obter(User),
            id,
            launchId,
            CorrelacaoHttp.Obter(HttpContext),
            cancellationToken));

    [HttpPost("{id:guid}/authorizations")]
    [Authorize(Policy = PoliticasAutorizacao.Administrador)]
    public async Task<IActionResult> ConcederAutorizacao(
        Guid id,
        [FromBody] RequisicaoConcessaoAutorizacao requisicao,
        CancellationToken cancellationToken)
    {
        var resultado = await _autorizacoes.ConcederAsync(
            AtorHttp.Obter(User),
            id,
            requisicao,
            ParametrosHttp.LerChaveIdempotencia(Request),
            CorrelacaoHttp.Obter(HttpContext),
            cancellationToken);

        return Created(resultado.Location, resultado);
    }

    [HttpPost("{id:guid}/authorizations/{authorizationId:guid}/revoke")]
    [Authorize(Policy = PoliticasAutorizacao.Administrador)]
    public async Task<IActionResult> RevogarAutorizacao(
        Guid id,
        Guid authorizationId,
        [FromBody] RequisicaoRevogacaoAutorizacao requisicao,
        CancellationToken cancellationToken)
    {
        var resultado = await _autorizacoes.RevogarAsync(
            AtorHttp.Obter(User),
            id,
            authorizationId,
            requisicao,
            ParametrosHttp.LerChaveIdempotencia(Request),
            CorrelacaoHttp.Obter(HttpContext),
            cancellationToken);

        return Created($"/api/v1/withdrawals/{id}/authorizations/{authorizationId}", resultado);
    }

    [HttpGet("{id:guid}/authorizations/{authorizationId:guid}/justification")]
    [Authorize(Policy = PoliticasAutorizacao.Administrador)]
    public async Task<IActionResult> JustificativaAutorizacao(
        Guid id,
        Guid authorizationId,
        CancellationToken cancellationToken) =>
        Ok(await _autorizacoes.LerJustificativaAsync(
            AtorHttp.Obter(User),
            id,
            authorizationId,
            CorrelacaoHttp.Obter(HttpContext),
            cancellationToken));

    /// <summary>
    /// Prontidão informativa da entrega, restrita a Caixa/Administrador. Devolve
    /// estado, saldo, conferência, presença da vistoria, condições, bloqueios e
    /// versões-base, sem texto, justificativa ou documento. Não reserva a
    /// entrega.
    /// </summary>
    [HttpGet("{id:guid}/delivery-readiness")]
    [Authorize(Policy = PoliticasAutorizacao.Operador)]
    [ProducesResponseType<ProntidaoEntrega>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ProntidaoEntrega(Guid id, CancellationToken cancellationToken) =>
        Ok(await _entrega.ObterProntidaoAsync(
            AtorHttp.Obter(User),
            id,
            cancellationToken));

    /// <summary>
    /// Confirma explicitamente a entrega, gravando entrega, consumos,
    /// encerramentos, fim da ocupação, liberação da vaga, histórico, auditoria
    /// e recibo idempotente na mesma transação. Restrita a Caixa/Administrador.
    /// Respostas: 201; 400 validação; 403 perfil; 404 pedido; 409 conflito ou já
    /// entregue; 422 condição de negócio não satisfeita.
    /// </summary>
    [HttpPost("{id:guid}/delivery")]
    [Authorize(Policy = PoliticasAutorizacao.Operador)]
    [ProducesResponseType<ResultadoEntrega>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ConfirmarEntrega(
        Guid id,
        [FromBody] RequisicaoConfirmarEntrega requisicao,
        CancellationToken cancellationToken)
    {
        var resultado = await _entrega.ConfirmarAsync(
            AtorHttp.Obter(User),
            id,
            requisicao,
            ParametrosHttp.LerChaveIdempotencia(Request),
            CorrelacaoHttp.Obter(HttpContext),
            cancellationToken);

        return Created(resultado.Location, resultado);
    }
}

/// <summary>Corpo do início de busca com a versão esperada do pedido.</summary>
public sealed record IniciarBuscaRequisicao(byte[]? VersaoEsperada);
