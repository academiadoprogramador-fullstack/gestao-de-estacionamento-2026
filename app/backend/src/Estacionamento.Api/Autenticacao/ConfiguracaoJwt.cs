using Estacionamento.Application.Configuracao;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Estacionamento.Api.Autenticacao;

/// <summary>
/// Fixa a validação em HS256 com <c>kid</c> resolvido somente no mapa local de
/// chaves versionadas, emissor, audiência, expiração e tolerância de 30 s.
/// </summary>
public static class ConfiguracaoJwt
{
    public static IServiceCollection AdicionarAutenticacaoJwt(
        this IServiceCollection services,
        IConfiguration configuracao)
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        // Os parâmetros são montados quando o handler JWT é resolvido, depois
        // que toda a configuração do ambiente (inclusive testes) foi aplicada.
        services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IConfiguration>((options, configuracaoAtual) =>
            {
                var jwt = new AutenticacaoJwtOptions();
                configuracaoAtual.GetSection(AutenticacaoJwtOptions.Secao).Bind(jwt);

                options.MapInboundClaims = false;
                options.TokenValidationParameters = CriarParametros(jwt);
                options.EventsType = typeof(EventosAutenticacaoJwt);
            });

        services.AddScoped<EventosAutenticacaoJwt>();
        services.AddScoped<ValidadorSessaoJwt>();

        return services;
    }

    public static TokenValidationParameters CriarParametros(AutenticacaoJwtOptions jwt) =>
        new()
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Emissor,
            ValidateAudience = true,
            ValidAudience = jwt.Audiencia,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(jwt.ToleranciaRelogioSegundos),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            IssuerSigningKeyResolver = (_, _, kid, _) =>
            {
                if (string.IsNullOrWhiteSpace(kid) ||
                    !jwt.Chaves.TryGetValue(kid, out var valor) ||
                    !ValidadorChaveSimetrica.PossuiFormatoValido(valor))
                {
                    return [];
                }

                return [new SymmetricSecurityKey(ValidadorChaveSimetrica.Decodificar(valor))];
            },
            NameClaimType = "sub",
            RoleClaimType = System.Security.Claims.ClaimTypes.Role
        };
}
