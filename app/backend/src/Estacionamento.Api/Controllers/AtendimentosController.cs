using Estacionamento.Api.Autenticacao;
using Estacionamento.Api.Infra;
using Estacionamento.Application.Atendimentos;
using Estacionamento.Application.Comum;
using Estacionamento.Application.Entrega;
using Estacionamento.Application.Fotos;
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
    private readonly IServicoFotosVistoria _fotos;

    public AtendimentosController(
        IServicoAtendimentos atendimentos,
        IServicoConsultaAtendimentos consulta,
        IServicoRetiradas retiradas,
        IServicoVistoriaSaida vistoriaSaida,
        IServicoFotosVistoria fotos)
    {
        _atendimentos = atendimentos;
        _consulta = consulta;
        _retiradas = retiradas;
        _vistoriaSaida = vistoriaSaida;
        _fotos = fotos;
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
    /// Lista os metadados das fotos disponíveis da vistoria. O conteúdo e as
    /// chaves do bucket nunca entram nesta projeção.
    /// </summary>
    [HttpGet("{id:guid}/inspections/{tipo}/photos")]
    [Authorize(Policy = PoliticasAutorizacao.Funcionario)]
    [ProducesResponseType<ListaFotosVistoria>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListarFotos(
        Guid id,
        string tipo,
        CancellationToken cancellationToken) =>
        Ok(await _fotos.ListarAsync(
            AtorHttp.Obter(User),
            id,
            tipo,
            cancellationToken));

    /// <summary>
    /// Anexa uma única foto à vistoria textual já salva. Entrada restrita a
    /// Caixa/Administrador; saída disponível aos três perfis. Exige
    /// <c>Idempotency-Key</c> e a versão-base do atendimento no formulário.
    /// </summary>
    [HttpPost("{id:guid}/inspections/{tipo}/photos")]
    [Authorize(Policy = PoliticasAutorizacao.Funcionario)]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [ProducesResponseType<ResultadoFoto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> EnviarFoto(
        Guid id,
        string tipo,
        [FromForm] EnviarFotoFormulario formulario,
        CancellationToken cancellationToken)
    {
        if (formulario.Arquivo is null)
        {
            throw ExcecaoAplicacao.Validacao(
                "photo_required",
                "Envie exatamente uma foto JPEG, PNG ou WebP estática.");
        }

        var versao = LerVersao(formulario.VersaoAtendimentoEsperada);

        var resultado = await _fotos.EnviarAsync(
            AtorHttp.Obter(User),
            id,
            tipo,
            new ArquivoFoto(
                formulario.Arquivo.FileName,
                formulario.Arquivo.ContentType,
                formulario.Arquivo.Length,
                formulario.Arquivo.OpenReadStream()),
            versao,
            ParametrosHttp.LerChaveIdempotencia(Request),
            CorrelacaoHttp.Obter(HttpContext),
            cancellationToken);

        return Created(resultado.Location, resultado);
    }

    /// <summary>
    /// Leitura explícita e auditada do conteúdo. A auditoria é confirmada antes
    /// de abrir o stream; o MIME é do servidor e a resposta nunca é cacheada.
    /// </summary>
    [HttpGet("{id:guid}/inspections/{tipo}/photos/{fotoId:guid}/content")]
    [Authorize(Policy = PoliticasAutorizacao.Funcionario)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ConteudoFoto(
        Guid id,
        string tipo,
        Guid fotoId,
        CancellationToken cancellationToken)
    {
        var conteudo = await _fotos.AbrirConteudoAsync(
            AtorHttp.Obter(User),
            id,
            tipo,
            fotoId,
            CorrelacaoHttp.Obter(HttpContext),
            cancellationToken);

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.ContentDisposition = $"inline; filename=\"{conteudo.NomeArquivo}\"";

        return new FileStreamResult(conteudo.Conteudo, conteudo.Mime)
        {
            EnableRangeProcessing = false
        };
    }

    private static byte[] LerVersao(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            throw ExcecaoAplicacao.Validacao(
                "stale_attendance_version",
                "Informe a versão atual do atendimento para anexar a foto.");
        }

        try
        {
            return Convert.FromBase64String(valor);
        }
        catch (FormatException)
        {
            throw ExcecaoAplicacao.Validacao(
                "stale_attendance_version",
                "A versão do atendimento é inválida. Recarregue o estado atual.");
        }
    }

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

/// <summary>Formulário multipart de uma única foto; versão-base vem do cliente.</summary>
public sealed record EnviarFotoFormulario(
    IFormFile? Arquivo,
    string? VersaoAtendimentoEsperada);
