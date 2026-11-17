namespace Estacionamento.Api.Infra;

/// <summary>
/// Correlação gerada pelo servidor para cada requisição. O valor do cliente não
/// é aceito como identidade nem como correlação confiável.
/// </summary>
public static class CorrelacaoHttp
{
    private const string Chave = "correlacao";

    public static string Obter(HttpContext contexto)
    {
        if (contexto.Items.TryGetValue(Chave, out var valor) && valor is string correlacao)
        {
            return correlacao;
        }

        correlacao = Guid.NewGuid().ToString("N");
        contexto.Items[Chave] = correlacao;
        return correlacao;
    }

    public static void Definir(HttpContext contexto)
    {
        var correlacao = Obter(contexto);
        contexto.Response.Headers["X-Correlation-Id"] = correlacao;
    }
}

/// <summary>
/// Gera a correlação antes dos demais middlewares e a devolve no cabeçalho.
/// </summary>
public sealed class MiddlewareCorrelacao
{
    private readonly RequestDelegate _proximo;

    public MiddlewareCorrelacao(RequestDelegate proximo)
    {
        _proximo = proximo;
    }

    public async Task InvokeAsync(HttpContext contexto)
    {
        CorrelacaoHttp.Definir(contexto);
        await _proximo(contexto);
    }
}
