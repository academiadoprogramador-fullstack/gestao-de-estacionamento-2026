using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Estacionamento.IntegrationTests;

/// <summary>
/// Utilitários dos cenários da Fase 3. Cada cliente mantém o próprio bearer; o
/// servidor continua sendo a autoridade de perfil, estado e cálculo.
/// </summary>
internal static class Fase3Apoio
{
    public static FabricaApiTeste NovaFabrica(
        Dictionary<string, string?>? configuracaoExtra = null) =>
        Fase2Apoio.NovaFabrica(configuracaoExtra);

    public static async Task<Guid> PrepararAtendimentoAsync(
        HttpClient administrador,
        HttpClient caixa,
        string vaga = "A-01",
        string placa = "ABC1D23",
        decimal tarifa = 20.00m)
    {
        using var espaco = await Fase2Apoio.CriarVagaAsync(administrador, vaga);
        await Fase2Apoio.PublicarTarifaAsync(administrador, tarifa);
        return await RegistrarEntradaAsync(caixa, espaco.RootElement.GetProperty("id").GetGuid(), placa);
    }

    public static async Task<Guid> PrepararAtendimentoComTarifaPublicadaAsync(
        HttpClient administrador,
        HttpClient caixa,
        string vaga,
        string placa,
        decimal tarifa = 20.00m)
    {
        using var espaco = await Fase2Apoio.CriarVagaAsync(administrador, vaga);
        return await RegistrarEntradaAsync(caixa, espaco.RootElement.GetProperty("id").GetGuid(), placa);
    }

    public static async Task<Guid> RegistrarEntradaAsync(
        HttpClient caixa,
        Guid vagaId,
        string placa)
    {
        using var resposta = await Fase2Apoio.RegistrarEntradaAsync(
            caixa,
            vagaId,
            Guid.NewGuid(),
            placa: placa);

        Assert.AreEqual(
            HttpStatusCode.Created,
            resposta.StatusCode,
            await resposta.Content.ReadAsStringAsync());

        using var documento = await Fase2Apoio.LerJsonAsync(resposta);
        return documento.RootElement.GetProperty("atendimentoId").GetGuid();
    }

    /// <summary>
    /// Desloca o instante de entrada no banco isolado para exercitar as
    /// fronteiras de 24 horas sem esperar tempo real.
    /// </summary>
    public static void DeslocarEntrada(
        FabricaApiTeste fabrica,
        Guid atendimentoId,
        TimeSpan atras)
    {
        SqlTeste.Executar(
            fabrica.NomeBanco,
            $"UPDATE [Atendimentos] SET [EntradaUtc] = DATEADD(second, -{Convert.ToInt64(atras.TotalSeconds)}, SYSDATETIMEOFFSET()) WHERE [Id] = '{atendimentoId}';");
    }

    public static Task<HttpResponseMessage> RegistrarPedidoAsync(
        HttpClient operador,
        Guid atendimentoId,
        Guid? chave = null,
        bool titularPresente = true,
        string conferencia = "aprovada",
        object? extras = null)
    {
        var corpo = new Dictionary<string, object?>
        {
            ["titularPresente"] = titularPresente,
            ["conferencia"] = conferencia
        };

        if (extras is not null)
        {
            foreach (var propriedade in JsonSerializer.SerializeToElement(extras).EnumerateObject())
            {
                corpo[propriedade.Name] = propriedade.Value;
            }
        }

        var requisicao = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/attendances/{atendimentoId}/withdrawal")
        {
            Content = JsonContent.Create(corpo)
        };

        requisicao.Headers.Add("Idempotency-Key", (chave ?? Guid.NewGuid()).ToString());
        return operador.SendAsync(requisicao);
    }

    public static async Task<Guid> RegistrarPedidoConfirmadoAsync(
        HttpClient operador,
        Guid atendimentoId,
        Guid? chave = null,
        string conferencia = "aprovada")
    {
        using var resposta = await RegistrarPedidoAsync(
            operador,
            atendimentoId,
            chave,
            conferencia: conferencia);

        Assert.AreEqual(
            HttpStatusCode.Created,
            resposta.StatusCode,
            await resposta.Content.ReadAsStringAsync());

        using var documento = await Fase2Apoio.LerJsonAsync(resposta);
        return documento.RootElement.GetProperty("pedidoId").GetGuid();
    }

    public static async Task<HttpResponseMessage> IniciarBuscaAsync(
        HttpClient funcionario,
        Guid pedidoId,
        string versaoEsperada)
    {
        var requisicao = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/withdrawals/{pedidoId}/search")
        {
            Content = JsonContent.Create(new { versaoEsperada })
        };

        return await funcionario.SendAsync(requisicao);
    }

    public static Task<HttpResponseMessage> PagarAsync(
        HttpClient operador,
        Guid pedidoId,
        decimal valor,
        string forma,
        int revisaoEsperada,
        Guid? chave = null) =>
        EnviarComChaveAsync(
            operador,
            HttpMethod.Post,
            $"/api/v1/withdrawals/{pedidoId}/payments",
            new { valor, forma, revisaoEsperada },
            chave);

    public static Task<HttpResponseMessage> DescontarAsync(
        HttpClient administrador,
        Guid pedidoId,
        decimal valor,
        string justificativa,
        int revisaoEsperada,
        Guid? chave = null) =>
        EnviarComChaveAsync(
            administrador,
            HttpMethod.Post,
            $"/api/v1/withdrawals/{pedidoId}/discounts",
            new { valor, justificativa, revisaoEsperada },
            chave);

    public static Task<HttpResponseMessage> EstornarAsync(
        HttpClient administrador,
        Guid pedidoId,
        Guid pagamentoId,
        decimal valor,
        string justificativa,
        int revisaoEsperada,
        Guid? chave = null) =>
        EnviarComChaveAsync(
            administrador,
            HttpMethod.Post,
            $"/api/v1/withdrawals/{pedidoId}/refunds",
            new { pagamentoId, valor, justificativa, revisaoEsperada },
            chave);

    public static Task<HttpResponseMessage> ConcederAutorizacaoAsync(
        HttpClient administrador,
        Guid pedidoId,
        string tipo,
        string? justificativa,
        string versaoEsperada,
        Guid? chave = null) =>
        EnviarComChaveAsync(
            administrador,
            HttpMethod.Post,
            $"/api/v1/withdrawals/{pedidoId}/authorizations",
            new { tipo, justificativa, versaoEsperada },
            chave);

    public static Task<HttpResponseMessage> RevogarAutorizacaoAsync(
        HttpClient administrador,
        Guid pedidoId,
        Guid autorizacaoId,
        string? justificativa,
        Guid? chave = null) =>
        EnviarComChaveAsync(
            administrador,
            HttpMethod.Post,
            $"/api/v1/withdrawals/{pedidoId}/authorizations/{autorizacaoId}/revoke",
            new { justificativa },
            chave);

    public static async Task<JsonDocument> CaixaAsync(
        HttpClient operador,
        Guid pedidoId,
        HttpStatusCode esperado = HttpStatusCode.OK)
    {
        var resposta = await operador.GetAsync($"/api/v1/withdrawals/{pedidoId}/cashier");
        Assert.AreEqual(esperado, resposta.StatusCode, await resposta.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
    }

    public static async Task<JsonDocument> DetalheAsync(
        HttpClient funcionario,
        Guid pedidoId,
        HttpStatusCode esperado = HttpStatusCode.OK)
    {
        var resposta = await funcionario.GetAsync($"/api/v1/withdrawals/{pedidoId}");
        Assert.AreEqual(esperado, resposta.StatusCode, await resposta.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
    }

    public static async Task<JsonDocument> FilaAsync(
        HttpClient funcionario,
        string? estado = null,
        int pagina = 1,
        int tamanho = 20)
    {
        var parametros = new List<string>
        {
            $"page={pagina}",
            $"pageSize={tamanho}"
        };

        if (estado is not null)
        {
            parametros.Add($"state={Uri.EscapeDataString(estado)}");
        }

        var resposta = await funcionario.GetAsync(
            $"/api/v1/withdrawals?{string.Join('&', parametros)}");

        Assert.AreEqual(HttpStatusCode.OK, resposta.StatusCode, await resposta.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
    }

    public static async Task<HttpResponseMessage> EnviarComChaveAsync(
        HttpClient cliente,
        HttpMethod metodo,
        string caminho,
        object corpo,
        Guid? chave)
    {
        var requisicao = new HttpRequestMessage(metodo, caminho)
        {
            Content = JsonContent.Create(corpo)
        };

        requisicao.Headers.Add("Idempotency-Key", (chave ?? Guid.NewGuid()).ToString());
        return await cliente.SendAsync(requisicao);
    }

    public static async Task<HttpResponseMessage> LerJustificativaLancamentoAsync(
        HttpClient cliente,
        Guid pedidoId,
        Guid lancamentoId) =>
        await cliente.GetAsync(
            $"/api/v1/withdrawals/{pedidoId}/launches/{lancamentoId}/justification");

    public static async Task<HttpResponseMessage> LerJustificativaAutorizacaoAsync(
        HttpClient cliente,
        Guid pedidoId,
        Guid autorizacaoId) =>
        await cliente.GetAsync(
            $"/api/v1/withdrawals/{pedidoId}/authorizations/{autorizacaoId}/justification");

    public static async Task<Guid> ObterPagamentoIdAsync(
        HttpClient operador,
        Guid pedidoId)
    {
        using var caixa = await CaixaAsync(operador, pedidoId);
        return caixa.RootElement.GetProperty("lancamentos")
            .EnumerateArray()
            .Single(item => item.GetProperty("tipo").GetString() == "pagamento")
            .GetProperty("id")
            .GetGuid();
    }

    public static async Task<Guid> ObterLancamentoIdAsync(
        HttpClient operador,
        Guid pedidoId,
        string tipo)
    {
        using var caixa = await CaixaAsync(operador, pedidoId);
        return caixa.RootElement.GetProperty("lancamentos")
            .EnumerateArray()
            .Single(item => item.GetProperty("tipo").GetString() == tipo)
            .GetProperty("id")
            .GetGuid();
    }

    public static string VersaoDoPedido(JsonDocument detalhe) =>
        detalhe.RootElement.GetProperty("versaoPedido").GetString()!;

    public static string VersaoDoDetalheOperacional(JsonDocument detalhe) =>
        detalhe.RootElement.GetProperty("versaoPedido").GetString()!;

    public static int RevisaoDaRazao(JsonDocument caixa) =>
        caixa.RootElement.GetProperty("revisaoRazao").GetInt32();

    public static decimal Saldo(JsonDocument caixa) =>
        caixa.RootElement.GetProperty("saldo").GetDecimal();

    public static async Task<string?> CodigoAsync(HttpResponseMessage resposta) =>
        await Fase2Apoio.CodigoDoErroAsync(resposta);
}
