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
