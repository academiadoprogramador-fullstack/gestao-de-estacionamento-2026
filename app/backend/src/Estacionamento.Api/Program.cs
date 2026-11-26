using System.Threading.RateLimiting;
using Estacionamento.Api.Autenticacao;
using Estacionamento.Api.Configuracao;
using Estacionamento.Api.Inicializacao;
using Estacionamento.Api.Infra;
using Estacionamento.Application.Auditoria;
using Estacionamento.Application.Configuracao;
using Estacionamento.Domain.Autenticacao;
using Estacionamento.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

PrecedenciaConfiguracao.AplicarRestricaoDeUserSecrets(
    builder.Configuration,
    builder.Environment.EnvironmentName,
    typeof(Program).Assembly.GetName().Name);

builder.Services.AdicionarInfraestrutura(builder.Configuration, builder.Environment.EnvironmentName);
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Propriedades desconhecidas (tarifa, horário, operador, identificador)
        // são rejeitadas em vez de silenciosamente ignoradas.
        options.JsonSerializerOptions.UnmappedMemberHandling =
            System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow;
    });
builder.Services.AdicionarAutenticacaoJwt(builder.Configuration);
builder.Services.AdicionarAutorizacaoPorPerfil();
builder.Services.AddSingleton<IRegistroBootstrap, RegistroBootstrap>();
builder.Services.AddScoped<BootstrapDesenvolvimento>();
builder.Services.AddScoped<ValidadorConfiguracaoInicial>();
builder.Services.AddHostedService<ManutencaoFotosHostedService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy(PoliticaCors.Nome, politica =>
    {
        // Lido no momento da resolução da política para respeitar a
        // configuração final aplicada pelo ambiente de teste.
        var origensPermitidas = builder.Configuration
            .GetSection($"{CorsOptions.Secao}:OrigensPermitidas")
            .Get<string[]>() ?? [];

        if (origensPermitidas.Length > 0)
        {
            politica.WithOrigins(origensPermitidas);
        }

        politica
            .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
            .WithHeaders("Authorization", "Content-Type", "Idempotency-Key")
            .WithExposedHeaders("X-Correlation-Id")
            .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(PoliticasRateLimit.Login, contexto =>
        RateLimitPartition.GetFixedWindowLimiter(
            contexto.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
            _ => new FixedWindowRateLimiterOptions
            {
                // 5/min em uso normal; testes podem elevar o limite para
                // exercitar outros fluxos sem colidir com a política.
                PermitLimit = LerLimiteLogin(builder.Configuration),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.OnRejected = async (contexto, cancellationToken) =>
    {
        contexto.HttpContext.Response.Headers.RetryAfter = "60";

        try
        {
            var auditoria = contexto.HttpContext.RequestServices.GetService<IServicoAuditoria>();
            if (auditoria is not null)
            {
                await auditoria.RegistrarAsync(
                    new EventoAuditoriaAutenticacao(
                        TiposEventoAuditoria.LoginLimitado,
                        null,
                        null,
                        CorrelacaoHttp.Obter(contexto.HttpContext),
                        "limite_por_ip",
                        DateTimeOffset.UtcNow),
                    cancellationToken);
            }
        }
        catch
        {
            // A indisponibilidade da auditoria não pode transformar o 429 em
            // erro interno nem permitir prosseguir com o login.
        }

        await RespostasProblema.EscreverAsync(
            contexto.HttpContext,
            StatusCodes.Status429TooManyRequests,
            "limite_login_excedido",
            "Muitas tentativas de login. Tente novamente em um minuto.");

    };
});

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = contexto =>
    {
        var problema = RespostasProblema.Criar(
            contexto.HttpContext,
            StatusCodes.Status400BadRequest,
            "requisicao_invalida",
            "A requisição contém dados inválidos.");

        problema.Extensions["errors"] = contexto.ModelState
            .Where(entrada => entrada.Value?.Errors.Count > 0)
            .ToDictionary(
                entrada => entrada.Key,
                entrada => entrada.Value!.Errors.Select(erro => erro.ErrorMessage).ToArray());

        return new ObjectResult(problema)
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { "application/problem+json" }
        };
    };
});

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddOpenApi();
}

var app = builder.Build();

app.UseMiddleware<MiddlewareCorrelacao>();
app.UseMiddleware<MiddlewareErros>();
app.UseMiddleware<MiddlewareSemCache>();

if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}

app.UseCors(PoliticaCors.Nome);
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi("/openapi/{documentName}.json")
        .RequireAuthorization(PoliticasAutorizacao.Administrador);
}

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

try
{
    using (var escopo = app.Services.CreateScope())
    {
        escopo.ServiceProvider.GetRequiredService<ValidadorConfiguracaoInicial>().Validar();
        await escopo.ServiceProvider.GetRequiredService<BootstrapDesenvolvimento>().ExecutarAsync();
    }
}
catch (OptionsValidationException excecao)
{
    app.Logger.LogCritical(
        "Configuração inválida. Inicialização interrompida: {Falhas}",
        string.Join(" | ", excecao.Failures));
    throw new InicializacaoException("Configuração inválida. A API não iniciará.", excecao);
}
catch (InvalidOperationException excecao)
{
    app.Logger.LogCritical("Inicialização interrompida: {Motivo}", excecao.Message);
    throw new InicializacaoException("Inicialização interrompida por configuração ou bootstrap inválido.", excecao);
}
catch (Exception excecao)
{
    app.Logger.LogCritical(
        "Inicialização interrompida por falha em {Tipo}.",
        excecao.GetType().Name);
    throw new InicializacaoException("Inicialização interrompida por falha não tratada.", excecao);
}

await app.RunAsync();

static int LerLimiteLogin(IConfiguration configuracao)
{
    var limite = configuracao.GetValue("Autenticacao:LimiteTentativasLoginPorMinuto", 5);
    return limite > 0 ? limite : 5;
}

/// <summary>Ponto de entrada exposto para os testes de integração.</summary>
public partial class Program;
