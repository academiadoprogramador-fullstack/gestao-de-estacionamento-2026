using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Estacionamento.Application.Comum;

namespace Estacionamento.Api.Autenticacao;

/// <summary>
/// Resolve o ator autenticado a partir dos claims do JWT validado. Nenhum
/// identificador de ator é aceito do corpo da requisição.
/// </summary>
public static class AtorHttp
{
    public static AtorAtual Obter(ClaimsPrincipal usuario)
    {
        var sub = usuario.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (!Guid.TryParse(sub, out var id))
        {
            throw ExcecaoAplicacao.AcessoNegado("nao_autenticado");
        }

        var perfis = usuario
            .FindAll(ClaimTypes.Role)
            .Select(claim => claim.Value)
            .Where(valor => !string.IsNullOrWhiteSpace(valor))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new AtorAtual(id, perfis);
    }
}
