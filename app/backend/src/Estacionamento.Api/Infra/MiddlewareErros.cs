using Estacionamento.Application.Comum;
using Microsoft.EntityFrameworkCore;

namespace Estacionamento.Api.Infra;

/// <summary>
/// Respostas da API nunca são armazenáveis em cache, inclusive sucessos de
/// sessão, erros e negações.
/// </summary>
public sealed class MiddlewareSemCache
{
    private readonly RequestDelegate _proximo;

    public MiddlewareSemCache(RequestDelegate proximo)
    {
        _proximo = proximo;
    }

    public async Task InvokeAsync(HttpContext contexto)
    {
        contexto.Response.OnStarting(() =>
        {
            if (contexto.Request.Path.StartsWithSegments("/api") ||
                contexto.Request.Path.StartsWithSegments("/openapi"))
            {
                contexto.Response.Headers.CacheControl = "no-store";
                contexto.Response.Headers.Pragma = "no-cache";
            }

            return Task.CompletedTask;
        });

        await _proximo(contexto);
    }
}

/// <summary>
/// Converte exceções e respostas de erro não tratadas em Problem Details sem
/// stack trace. Falhas internas são registradas apenas por tipo e correlação.
/// </summary>
public sealed class MiddlewareErros
{
    private readonly RequestDelegate _proximo;
    private readonly ILogger<MiddlewareErros> _logger;

    public MiddlewareErros(RequestDelegate proximo, ILogger<MiddlewareErros> logger)
    {
        _proximo = proximo;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext contexto)
    {
        try
        {
            await _proximo(contexto);

            if (contexto.Response.StatusCode >= 400 && !contexto.Response.HasStarted)
            {
                var codigo = contexto.Response.StatusCode switch
                {
                    StatusCodes.Status400BadRequest => "requisicao_invalida",
                    StatusCodes.Status401Unauthorized => "nao_autenticado",
                    StatusCodes.Status403Forbidden => "acesso_negado",
                    StatusCodes.Status404NotFound => "nao_encontrado",
                    StatusCodes.Status405MethodNotAllowed => "metodo_nao_permitido",
                    StatusCodes.Status429TooManyRequests => "limite_excedido",
                    _ => "erro_http"
                };

                await RespostasProblema.EscreverAsync(
                    contexto,
                    contexto.Response.StatusCode,
                    codigo,
                    "A requisição não pôde ser concluída.");
            }
        }
        catch (OperationCanceledException) when (contexto.RequestAborted.IsCancellationRequested)
        {
            // Requisição cancelada pelo cliente: não gera erro nem log de falha.
        }
        catch (ExcecaoAplicacao excecao)
        {
            var status = excecao.Tipo switch
            {
                TipoFalha.Validacao => StatusCodes.Status400BadRequest,
                TipoFalha.AcessoNegado => StatusCodes.Status403Forbidden,
                TipoFalha.NaoEncontrado => StatusCodes.Status404NotFound,
                TipoFalha.Conflito => StatusCodes.Status409Conflict,
                TipoFalha.RegraNaoSatisfeita => StatusCodes.Status422UnprocessableEntity,
                _ => StatusCodes.Status500InternalServerError
            };

            _logger.LogWarning(
                "Operação recusada ({Codigo}). Correlação: {Correlacao}",
                excecao.Codigo,
                CorrelacaoHttp.Obter(contexto));

            var problema = RespostasProblema.Criar(
                contexto,
                status,
                excecao.Codigo,
                excecao.Message);

            if (excecao.Erros is { Count: > 0 })
            {
                problema.Extensions["errors"] = excecao.Erros;
            }

            if (contexto.Response.HasStarted)
            {
                return;
            }

            contexto.Response.Headers.ContentLength = null;
            contexto.Response.StatusCode = status;
            contexto.Response.ContentType = "application/problem+json; charset=utf-8";
            contexto.Response.Headers.CacheControl = "no-store";
            contexto.Response.Headers.Pragma = "no-cache";
            contexto.Response.Headers["X-Correlation-Id"] = CorrelacaoHttp.Obter(contexto);

            await contexto.Response.WriteAsync(
                System.Text.Json.JsonSerializer.Serialize(
                    problema,
                    System.Text.Json.JsonSerializerOptions.Web));
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning(
                "Conflito de concorrência. Correlação: {Correlacao}",
                CorrelacaoHttp.Obter(contexto));

            await RespostasProblema.EscreverAsync(
                contexto,
                StatusCodes.Status409Conflict,
                "concurrency_conflict",
                "O registro foi alterado por outra operação. Recarregue o estado atual.");
        }
        catch (Exception excecao)
        {
            _logger.LogError(
                excecao,
                "Erro interno não tratado. Correlação: {Correlacao}",
                CorrelacaoHttp.Obter(contexto));

            await RespostasProblema.EscreverAsync(
                contexto,
                StatusCodes.Status500InternalServerError,
                "erro_interno",
                "Ocorreu um erro interno.");
        }
    }
}
