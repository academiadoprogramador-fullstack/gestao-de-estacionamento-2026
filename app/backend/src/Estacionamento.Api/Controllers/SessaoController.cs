using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Estacionamento.Api.Autenticacao;
using Estacionamento.Api.Infra;
using Estacionamento.Application.Autenticacao;
using Estacionamento.Infrastructure.Servicos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Estacionamento.Api.Controllers;

/// <summary>
/// Contratos de login, renovação, consulta e logout. Todas as respostas são
/// <c>no-store</c>; erros são genéricos e não enumeram contas.
/// </summary>
[ApiController]
[Route("api/v1/session")]
public sealed class SessaoController : ControllerBase
{
    private readonly IServicoSessao _servicoSessao;

    public SessaoController(IServicoSessao servicoSessao)
    {
        _servicoSessao = servicoSessao;
    }

    /// <summary>Autentica e-mail e senha e emite JWT e refresh.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(PoliticasRateLimit.Login)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequisicao requisicao,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(requisicao.Email) || string.IsNullOrWhiteSpace(requisicao.Senha))
        {
            return RespostasProblema.Resultado(
                HttpContext,
                StatusCodes.Status400BadRequest,
                "dados_invalidos",
                "Informe e-mail e senha.");
        }

        var resultado = await _servicoSessao.LoginAsync(
            new RequisicaoLogin(requisicao.Email, requisicao.Senha),
            CorrelacaoHttp.Obter(HttpContext),
            cancellationToken);

        return resultado.Sucesso
            ? Ok(MapearSessao(resultado))
            : FalhaGenerica();
    }

    /// <summary>Consome o refresh do corpo JSON e devolve novo JWT e refresh.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshRequisicao requisicao,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(requisicao.Refresh))
        {
            return RespostasProblema.Resultado(
                HttpContext,
                StatusCodes.Status400BadRequest,
                "dados_invalidos",
                "Refresh é obrigatório no corpo JSON.");
        }

        var resultado = await _servicoSessao.RenovarAsync(
            requisicao.Refresh,
            CorrelacaoHttp.Obter(HttpContext),
            cancellationToken);

        return resultado.Sucesso
            ? Ok(MapearSessao(resultado))
            : FalhaGenerica();
    }

    /// <summary>Consulta a sessão autenticada pelo bearer.</summary>
    [HttpGet]
    [Authorize(Policy = PoliticasAutorizacao.Funcionario)]
    public async Task<IActionResult> Obter(CancellationToken cancellationToken)
    {
        var sessao = await ObterSessaoAtualAsync(cancellationToken);
        if (sessao is null)
        {
            return RespostasProblema.Resultado(
                HttpContext,
                StatusCodes.Status401Unauthorized,
                "nao_autenticado",
                "Sessão inválida.");
        }

        return Ok(new
        {
            sessaoId = sessao.SessaoId,
            criadaEmUtc = sessao.CriadaEmUtc,
            expiraEmUtc = sessao.ExpiraEmUtc,
            versaoAcesso = sessao.VersaoAcesso,
            funcionario = MapearFuncionario(sessao.Funcionario)
        });
    }

    /// <summary>Revoga a sessão e os refreshes antes de responder.</summary>
    [HttpPost("logout")]
    [Authorize(Policy = PoliticasAutorizacao.Funcionario)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(GeradorTokenJwt.ClaimSessao), out var sessaoId))
        {
            return RespostasProblema.Resultado(
                HttpContext,
                StatusCodes.Status401Unauthorized,
                "nao_autenticado",
                "Sessão inválida.");
        }

        await _servicoSessao.LogoutAsync(sessaoId, CorrelacaoHttp.Obter(HttpContext), cancellationToken);
        return NoContent();
    }

    private async Task<SessaoAtual?> ObterSessaoAtualAsync(CancellationToken cancellationToken)
    {
        return Guid.TryParse(User.FindFirstValue(GeradorTokenJwt.ClaimSessao), out var sessaoId)
            ? await _servicoSessao.ObterSessaoAsync(sessaoId, cancellationToken)
            : null;
    }

    private IActionResult FalhaGenerica() =>
        RespostasProblema.Resultado(
            HttpContext,
            StatusCodes.Status401Unauthorized,
            "credenciais_invalidas",
            "Credenciais inválidas.");

    private static object MapearSessao(ResultadoLogin resultado) => new
    {
        tokenAcesso = resultado.TokenAcesso,
        refresh = resultado.Refresh,
        expiraAcessoEmUtc = resultado.ExpiraAcessoEmUtc,
        expiraSessaoEmUtc = resultado.ExpiraSessaoEmUtc,
        funcionario = resultado.Funcionario is null ? null : MapearFuncionario(resultado.Funcionario)
    };

    private static object MapearFuncionario(FuncionarioSessao funcionario) => new
    {
        id = funcionario.Id,
        nome = funcionario.Nome,
        email = funcionario.Email,
        perfis = funcionario.Perfis
    };
}

/// <summary>Credenciais de login recebidas no corpo JSON.</summary>
public sealed record LoginRequisicao(string Email, string Senha);

/// <summary>Refresh opaco recebido exclusivamente no corpo JSON.</summary>
public sealed record RefreshRequisicao(string Refresh);
