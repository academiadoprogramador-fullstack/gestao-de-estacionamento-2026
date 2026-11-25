using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Estacionamento.IntegrationTests;

/// <summary>
/// Utilitários dos cenários da Fase 4. O cliente nunca fornece ator, horário,
/// saldo ou liberação; apenas versões-base, texto normalizado e a chave
/// idempotente.
/// </summary>
internal static class Fase4Apoio
{
    public static FabricaApiTeste NovaFabrica(
        Dictionary<string, string?>? configuracaoExtra = null) =>
        Fase3Apoio.NovaFabrica(configuracaoExtra);

    public static async Task<string> VersaoAtendimentoAsync(
        HttpClient funcionario,
        Guid atendimentoId)
    {
        using var resposta = await funcionario.GetAsync($"/api/v1/attendances/{atendimentoId}");
        Assert.AreEqual(HttpStatusCode.OK, resposta.StatusCode, await resposta.Content.ReadAsStringAsync());
        using var detalhe = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return detalhe.RootElement.GetProperty("versaoAtendimento").GetString()!;
    }

    public static Task<HttpResponseMessage> RegistrarVistoriaAsync(
        HttpClient funcionario,
        Guid atendimentoId,
        string? texto,
        string? versaoAtendimento,
        Guid? chave = null,
        object? extras = null)
    {
        var corpo = new Dictionary<string, object?>
        {
            ["texto"] = texto,
            ["versaoAtendimentoEsperada"] = versaoAtendimento
        };

        if (extras is not null)
        {
            foreach (var propriedade in JsonSerializer.SerializeToElement(extras).EnumerateObject())
            {
                corpo[propriedade.Name] = propriedade.Value;
            }
        }

        return EnviarComChaveAsync(
            funcionario,
            HttpMethod.Post,
            $"/api/v1/attendances/{atendimentoId}/exit-inspection",
            corpo,
            chave);
    }

    public static async Task<JsonDocument> RegistrarVistoriaConfirmadaAsync(
        HttpClient funcionario,
        Guid atendimentoId,
        string texto = "Condições de saída sintéticas.")
    {
        var versao = await VersaoAtendimentoAsync(funcionario, atendimentoId);
        using var resposta = await RegistrarVistoriaAsync(funcionario, atendimentoId, texto, versao);
        Assert.AreEqual(HttpStatusCode.Created, resposta.StatusCode, await resposta.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
    }

    public static Task<HttpResponseMessage> LerVistoriaAsync(
        HttpClient funcionario,
        Guid atendimentoId) =>
        funcionario.GetAsync($"/api/v1/attendances/{atendimentoId}/exit-inspection");

    public static async Task<JsonDocument> ProntidaoAsync(
        HttpClient operador,
        Guid pedidoId,
        HttpStatusCode esperado = HttpStatusCode.OK)
    {
        var resposta = await operador.GetAsync($"/api/v1/withdrawals/{pedidoId}/delivery-readiness");
        Assert.AreEqual(esperado, resposta.StatusCode, await resposta.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
    }

    public static Task<HttpResponseMessage> ConfirmarEntregaAsync(
        HttpClient operador,
        Guid pedidoId,
        JsonDocument prontidao,
        Guid? chave = null,
        bool confirmacao = true,
        object? sobrescritas = null)
    {
        var versoes = prontidao.RootElement.GetProperty("versoes");
        var corpo = new Dictionary<string, object?>
        {
            ["confirmacao"] = confirmacao,
            ["versaoAtendimento"] = versoes.GetProperty("versaoAtendimento").GetString(),
            ["versaoPedido"] = versoes.GetProperty("versaoPedido").GetString(),
            ["versaoRazao"] = versoes.GetProperty("versaoRazao").GetString(),
            ["versaoControleDocumental"] = versoes.GetProperty("versaoControleDocumental").GetString(),
            ["versaoControleFinanceira"] = versoes.GetProperty("versaoControleFinanceira").GetString()
        };

        if (sobrescritas is not null)
        {
            foreach (var propriedade in JsonSerializer.SerializeToElement(sobrescritas).EnumerateObject())
            {
                corpo[propriedade.Name] = propriedade.Value;
            }
        }

        return EnviarComChaveAsync(
            operador,
            HttpMethod.Post,
            $"/api/v1/withdrawals/{pedidoId}/delivery",
            corpo,
            chave);
    }

    public static async Task<JsonDocument> ConfirmarEntregaConfirmadaAsync(
        HttpClient operador,
        Guid pedidoId,
        Guid? chave = null)
    {
        using var prontidao = await ProntidaoAsync(operador, pedidoId);
        using var resposta = await ConfirmarEntregaAsync(operador, pedidoId, prontidao, chave);
        Assert.AreEqual(HttpStatusCode.Created, resposta.StatusCode, await resposta.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
    }

    public static async Task<Guid> PrepararPedidoComVistoriaAsync(
        HttpClient administrador,
        HttpClient caixa,
        HttpClient? vistoriador = null,
        string vaga = "A-01",
        string placa = "ABC1D23",
        decimal tarifa = 20.00m,
        string conferencia = "aprovada")
    {
        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(
            administrador,
            caixa,
            vaga,
            placa,
            tarifa);
        var pedidoId = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(
            caixa,
            atendimentoId,
            conferencia: conferencia);
        await RegistrarVistoriaConfirmadaAsync(vistoriador ?? caixa, atendimentoId);
        return pedidoId;
    }

    public static Task<HttpResponseMessage> EnviarComChaveAsync(
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
        return cliente.SendAsync(requisicao);
    }

    public static async Task<string?> CodigoAsync(HttpResponseMessage resposta) =>
        await Fase2Apoio.CodigoDoErroAsync(resposta);

    public static string Versao(JsonDocument documento, string propriedade) =>
        documento.RootElement.GetProperty(propriedade).GetString()!;
}
