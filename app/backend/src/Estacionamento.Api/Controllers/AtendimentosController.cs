using Estacionamento.Api.Autenticacao;
using Estacionamento.Api.Infra;
using Estacionamento.Application.Atendimentos;
using Estacionamento.Application.Comum;
using Estacionamento.Application.Entrega;
using Estacionamento.Application.Retiradas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Estacionamento.Api.Controllers;

/// <summary>
/// Check-in transacional e consulta de atendimentos. Listagens e detalhe comum
/// nunca incluem documento ou vistoria; leituras sensíveis são explícitas. O
/// pedido de retirada vive no caminho do atendimento e não aceita documento,
/// foto ou dados de terceiro.
/// </summary>
[ApiController]
[Route("api/v1/attendances")]
public sealed class AtendimentosController : ControllerBase
{
    private readonly IServicoAtendimentos _atendimentos;
    private readonly IServicoConsultaAtendimentos _consulta;
    private readonly IServicoRetiradas _retiradas;
    private readonly IServicoVistoriaSaida _vistoriaSaida;

    public AtendimentosController(
        IServicoAtendimentos atendimentos,
        IServicoConsultaAtendimentos consulta,
        IServicoRetiradas retiradas,
        IServicoVistoriaSaida vistoriaSaida)
    {
        _atendimentos = atendimentos;
        _consulta = consulta;
        _retiradas = retiradas;
        _vistoriaSaida = vistoriaSaida;
    }

    [HttpPost]
    [Authorize(Policy = PoliticasAutorizacao.Operador)]
    public async Task<IActionResult> RegistrarEntrada(
        [FromBody] RegistrarEntradaRequisicao requisicao,
        CancellationToken cancellationToken)
    {
        var chave = ParametrosHttp.LerChaveIdempotencia(Request);

        var resultado = await _atendimentos.RegistrarEntradaAsync(
            AtorHttp.Obter(User),
            new RequisicaoRegistrarEntrada(
                requisicao.Placa ?? string.Empty,
                requisicao.Modelo ?? string.Empty,
                requisicao.Titular ?? string.Empty,
                requisicao.Documento ?? string.Empty,
                requisicao.Vistoria ?? string.Empty,
                requisicao.VagaId),
            chave,
            CorrelacaoHttp.Obter(HttpContext),
            cancellationToken);

        return Created(resultado.Location, resultado);
    }

    [HttpPost("{id:guid}/withdrawal")]
    [Authorize(Policy = PoliticasAutorizacao.Operador)]
    public async Task<IActionResult> RegistrarPedidoRetirada(
        Guid id,
        [FromBody] RequisicaoRegistrarPedidoRetirada requisicao,
        CancellationToken cancellationToken)
    {
        var chave = ParametrosHttp.LerChaveIdempotencia(Request);

        var resultado = await _retiradas.RegistrarPedidoAsync(
            AtorHttp.Obter(User),
            id,
            requisicao,
            chave,
            CorrelacaoHttp.Obter(HttpContext),
            cancellationToken);

        return Created(resultado.Location, resultado);
    }

    [HttpGet]
    [Authorize(Policy = PoliticasAutorizacao.Funcionario)]
    public async Task<IActionResult> Buscar(
        [FromQuery] string? plate,
        [FromQuery] string? identifier,
        [FromQuery] string? status,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var (pagina, tamanho) = ParametrosHttp.LerPaginacao(page, pageSize);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var filtroAbertos = status.Equals("aberto", StringComparison.Ordinal);
            var temBuscaExata = !string.IsNullOrWhiteSpace(plate) || !string.IsNullOrWhiteSpace(identifier);

            if (!filtroAbertos || temBuscaExata)
            {
                throw ExcecaoAplicacao.Validacao(
                    "filtro_invalido",
                    "Use status=aberto isoladamente ou informe exatamente um filtro de placa/identificador.");
            }

            return Ok(await _consulta.ListarAbertosAsync(
                AtorHttp.Obter(User),
                pagina,
                tamanho,
                cancellationToken));
        }

        var resultado = await _consulta.BuscarAsync(
            AtorHttp.Obter(User),
            plate,
            identifier,
            pagina,
            tamanho,
            cancellationToken);

        return Ok(resultado);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PoliticasAutorizacao.Funcionario)]
    public async Task<IActionResult> Detalhe(Guid id, CancellationToken cancellationToken) =>
        Ok(await _consulta.ObterDetalheAsync(AtorHttp.Obter(User), id, cancellationToken));

    [HttpGet("{id:guid}/history")]
    [Authorize(Policy = PoliticasAutorizacao.Funcionario)]
    public async Task<IActionResult> Historico(
        Guid id,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var (pagina, tamanho) = ParametrosHttp.LerPaginacao(page, pageSize);

        var resultado = await _consulta.ListarHistoricoAsync(
            AtorHttp.Obter(User),
            id,
            pagina,
            tamanho,
            cancellationToken);

        return Ok(resultado);
    }

    [HttpGet("{id:guid}/document")]
    [Authorize(Policy = PoliticasAutorizacao.Operador)]
    public async Task<IActionResult> Documento(Guid id, CancellationToken cancellationToken) =>
        Ok(await _consulta.LerDocumentoAsync(
            AtorHttp.Obter(User),
            id,
            CorrelacaoHttp.Obter(HttpContext),
            cancellationToken));

    [HttpGet("{id:guid}/entry-inspection")]
    [Authorize(Policy = PoliticasAutorizacao.Funcionario)]
    public async Task<IActionResult> VistoriaEntrada(Guid id, CancellationToken cancellationToken) =>
        Ok(await _consulta.LerVistoriaEntradaAsync(
            AtorHttp.Obter(User),
            id,
            CorrelacaoHttp.Obter(HttpContext),
            cancellationToken));

    /// <summary>
    /// Registra a vistoria textual de saída, única e imutável. Aceita os três
    /// perfis e exige <c>Idempotency-Key</c> e a versão-base do atendimento.
    /// Respostas: 201 com recibo mínimo; 400 texto/versão inválidos; 403 perfil;
    /// 404 atendimento; 409 vistoria existente, entrega concluída ou conflito.
    /// </summary>
    [HttpPost("{id:guid}/exit-inspection")]
    [Authorize(Policy = PoliticasAutorizacao.Funcionario)]
    [ProducesResponseType<ResultadoVistoriaSaida>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RegistrarVistoriaSaida(
        Guid id,
        [FromBody] RequisicaoVistoriaSaida requisicao,
        CancellationToken cancellationToken)
    {
        var resultado = await _vistoriaSaida.RegistrarAsync(
            AtorHttp.Obter(User),
            id,
            requisicao,
            ParametrosHttp.LerChaveIdempotencia(Request),
            CorrelacaoHttp.Obter(HttpContext),
            cancellationToken);

        return Created(resultado.Location, resultado);
    }

    /// <summary>
    /// Leitura explícita e auditada da vistoria de saída, disponível aos três
    /// perfis. A auditoria é confirmada antes de devolver o texto; a resposta
    /// nunca é armazenável em cache.
    /// </summary>
    [HttpGet("{id:guid}/exit-inspection")]
    [Authorize(Policy = PoliticasAutorizacao.Funcionario)]
    [ProducesResponseType<LeituraVistoriaSaida>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> VistoriaSaida(Guid id, CancellationToken cancellationToken) =>
        Ok(await _vistoriaSaida.LerAsync(
            AtorHttp.Obter(User),
            id,
            CorrelacaoHttp.Obter(HttpContext),
            cancellationToken));
}

/// <summary>
/// Campos de entrada aceitos do cliente. Não existe propriedade para tarifa,
/// horário, operador ou identificador; propriedades desconhecidas são
/// rejeitadas pela configuração estrita do serializador.
/// </summary>
public sealed record RegistrarEntradaRequisicao(
    string? Placa,
    string? Modelo,
    string? Titular,
    string? Documento,
    string? Vistoria,
    Guid VagaId);
