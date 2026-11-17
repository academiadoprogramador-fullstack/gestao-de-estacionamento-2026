using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Estacionamento.Api.Autenticacao;
using Estacionamento.Api.Infra;
using Estacionamento.Application.Autenticacao;
using Estacionamento.Infrastructure.Servicos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Estacionamento.Api.Controllers;

/// <summary>
/// Área de trabalho autenticada acessível aos três perfis. Nesta fase apenas
/// devolve os dados mínimos da sessão; nenhum dado financeiro é exposto.
/// </summary>
[ApiController]
[Route("api/v1/area-trabalho")]
[Authorize(Policy = PoliticasAutorizacao.Funcionario)]
public sealed class AreaTrabalhoController : ControllerBase
{
    private readonly IServicoSessao _servicoSessao;

    public AreaTrabalhoController(IServicoSessao servicoSessao)
    {
        _servicoSessao = servicoSessao;
    }

    [HttpGet]
    public async Task<IActionResult> Obter(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(GeradorTokenJwt.ClaimSessao), out var sessaoId))
        {
            return RespostasProblema.Resultado(
                HttpContext,
                StatusCodes.Status401Unauthorized,
                "nao_autenticado",
                "Sessão inválida.");
        }

        var sessao = await _servicoSessao.ObterSessaoAsync(sessaoId, cancellationToken);
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
            nome = sessao.Funcionario.Nome,
            perfis = sessao.Funcionario.Perfis,
            versaoAcesso = sessao.VersaoAcesso,
            expiraEmUtc = sessao.ExpiraEmUtc
        });
    }
}
