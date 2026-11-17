using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Estacionamento.Api.Infra;
using Estacionamento.Infrastructure.Servicos;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Estacionamento.Api.Autenticacao;

/// <summary>
/// Eventos do JWT bearer: valida a sessão no SQL a cada requisição e padroniza
/// desafios (401) e negações (403) como Problem Details não cacheáveis.
/// </summary>
public sealed class EventosAutenticacaoJwt : JwtBearerEvents
{
    public override async Task TokenValidated(TokenValidatedContext contexto)
    {
        var principal = contexto.Principal;
        var sub = principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var sid = principal?.FindFirstValue(GeradorTokenJwt.ClaimSessao);
        var versao = principal?.FindFirstValue(GeradorTokenJwt.ClaimVersaoAcesso);

        if (!Guid.TryParse(sub, out var usuarioId) ||
            !Guid.TryParse(sid, out var sessaoId) ||
            !long.TryParse(versao, out var versaoAcesso))
        {
            contexto.Fail("Claims mínimos de sessão ausentes ou inválidos.");
            return;
        }

        try
        {
            var validador = contexto.HttpContext.RequestServices.GetRequiredService<ValidadorSessaoJwt>();
            var valida = await validador.ValidarAsync(
                usuarioId,
                sessaoId,
                versaoAcesso,
                contexto.HttpContext.RequestAborted);

            if (!valida)
            {
                contexto.Fail("Sessão revogada, expirada ou incompatível.");
            }
        }
        catch
        {
            // SQL indisponível ou falha de consulta: nunca aceitar apenas a
            // assinatura do JWT como fallback.
            contexto.Fail("Sessão não pôde ser verificada.");
        }
    }

    public override async Task Challenge(JwtBearerChallengeContext contexto)
    {
        contexto.HandleResponse();
        await RespostasProblema.EscreverAsync(
            contexto.HttpContext,
            StatusCodes.Status401Unauthorized,
            "nao_autenticado",
            "Autenticação necessária.");
    }

    public override async Task Forbidden(ForbiddenContext contexto)
    {
        await RespostasProblema.EscreverAsync(
            contexto.HttpContext,
            StatusCodes.Status403Forbidden,
            "acesso_negado",
            "Acesso negado para o perfil autenticado.");
    }
}
