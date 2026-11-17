using System.Text.Json;
using Estacionamento.Application.Configuracao;
using Microsoft.AspNetCore.Mvc;

namespace Estacionamento.Api.Infra;

/// <summary>
/// Contrato único de erro da API. Não expõe stack trace, exceção, corpo de
/// requisição nem dados pessoais; toda resposta de erro recebe correlação.
/// </summary>
public static class RespostasProblema
{
    /// <summary>
    /// Cria o corpo Problem Details usado tanto pelo middleware quanto pelos
    /// controllers.
    /// </summary>
    public static ProblemDetails Criar(
        HttpContext contexto,
        int status,
        string codigo,
        string titulo,
        string? detalhe = null)
    {
        var problema = new ProblemDetails
        {
            Status = status,
            Title = titulo,
            Type = $"https://gestao-estacionamento.local/problems/{codigo}",
            Instance = contexto.Request.Path,
            Detail = detalhe
        };

        problema.Extensions["code"] = codigo;
        problema.Extensions["correlationId"] = CorrelacaoHttp.Obter(contexto);
        return problema;
    }

    public static IActionResult Resultado(
        HttpContext contexto,
        int status,
        string codigo,
        string titulo,
        string? detalhe = null) =>
        new ObjectResult(Criar(contexto, status, codigo, titulo, detalhe))
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" }
        };

    public static async Task EscreverAsync(
        HttpContext contexto,
        int status,
        string codigo,
        string titulo,
        string? detalhe = null)
    {
        if (contexto.Response.HasStarted)
        {
            return;
        }

        // Não usar Response.Clear: ele apagaria cabeçalhos de CORS,
        // correlação e Retry-After já definidos por outros middlewares.
        contexto.Response.Headers.ContentLength = null;
        contexto.Response.StatusCode = status;
        contexto.Response.ContentType = "application/problem+json; charset=utf-8";
        contexto.Response.Headers.CacheControl = "no-store";
        contexto.Response.Headers.Pragma = "no-cache";
        contexto.Response.Headers["X-Correlation-Id"] = CorrelacaoHttp.Obter(contexto);

        var problema = Criar(contexto, status, codigo, titulo, detalhe);
        await contexto.Response.WriteAsync(JsonSerializer.Serialize(problema, JsonSerializerOptions.Web));
    }
}
