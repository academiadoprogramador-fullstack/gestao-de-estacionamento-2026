using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Estacionamento.Application.Autenticacao;
using Estacionamento.Application.Configuracao;
using Estacionamento.Application.Tempo;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Estacionamento.Infrastructure.Servicos;

/// <summary>
/// Emite JWT HS256 com chave de 32 bytes por versão e <c>kid</c> correspondente
/// à versão ativa. O token não contém dados pessoais ou financeiros.
/// </summary>
public sealed class GeradorTokenJwt : IGeradorTokenJwt
{
    public const string ClaimVersaoAcesso = "versaoAcesso";
    public const string ClaimSessao = "sid";

    private readonly AutenticacaoJwtOptions _options;
    private readonly IRelogio _relogio;

    public GeradorTokenJwt(IOptions<AutenticacaoJwtOptions> options, IRelogio relogio)
    {
        _options = options.Value;
        _relogio = relogio;
    }

    public TokenJwt Emitir(
        Guid usuarioId,
        Guid sessaoId,
        long versaoAcesso,
        IEnumerable<string> perfis,
        DateTimeOffset? limiteAbsoluto = null)
    {
        var agora = _relogio.UtcNow;
        var expiracaoNatural = agora.AddMinutes(_options.DuracaoAcessoMinutos);
        var expiraEm = limiteAbsoluto.HasValue && limiteAbsoluto.Value < expiracaoNatural
            ? limiteAbsoluto.Value
            : expiracaoNatural;

        var chave = ValidadorChaveSimetrica.Decodificar(_options.Chaves[_options.VersaoAtiva]);
        var credenciais = new SigningCredentials(
            new SymmetricSecurityKey(chave),
            SecurityAlgorithms.HmacSha256);

        var cabecalho = new JwtHeader(credenciais);
        cabecalho[JwtHeaderParameterNames.Kid] = _options.VersaoAtiva;

        var carga = new JwtPayload
        {
            [JwtRegisteredClaimNames.Sub] = usuarioId.ToString(),
            [ClaimSessao] = sessaoId.ToString(),
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            [JwtRegisteredClaimNames.Iat] = agora.ToUnixTimeSeconds(),
            [ClaimVersaoAcesso] = versaoAcesso,
            [JwtRegisteredClaimNames.Iss] = _options.Emissor,
            [JwtRegisteredClaimNames.Aud] = _options.Audiencia,
            [JwtRegisteredClaimNames.Exp] = expiraEm.ToUnixTimeSeconds(),
            [JwtRegisteredClaimNames.Nbf] = agora.ToUnixTimeSeconds()
        };

        var perfisDistintos = perfis.Distinct(StringComparer.Ordinal).ToArray();
        if (perfisDistintos.Length > 0)
        {
            carga[ClaimTypes.Role] = perfisDistintos;
        }

        var token = new JwtSecurityToken(cabecalho, carga);
        return new TokenJwt(new JwtSecurityTokenHandler().WriteToken(token), expiraEm);
    }
}
