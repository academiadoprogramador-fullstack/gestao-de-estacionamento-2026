using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Estacionamento.IntegrationTests;

[TestClass]
public sealed class ContratosFase4Testes
{
    [TestMethod]
    public async Task RotasDeEntregaExigemSessaoValidaEAplicamCorsRestrito()
    {
        using var fabrica = Fase4Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);
        var (manobrista, _) = await Fase2Apoio.EntrarManobristaAsync(fabrica);
        using var anonimo = fabrica.CriarCliente();

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa);
        var pedidoId = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, atendimentoId);

        using var semSessao = await anonimo.GetAsync(
            $"/api/v1/withdrawals/{pedidoId}/delivery-readiness");
        Assert.AreEqual(HttpStatusCode.Unauthorized, semSessao.StatusCode);

        using var requisicaoInvalida = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/withdrawals/{pedidoId}/delivery")
        {
            Content = JsonContent.Create(new { confirmacao = true })
        };
        requisicaoInvalida.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "invalido");
        requisicaoInvalida.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var tokenInvalido = await anonimo.SendAsync(requisicaoInvalida);
        Assert.AreEqual(HttpStatusCode.Unauthorized, tokenInvalido.StatusCode);

        using var vistoriaAnonima = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/attendances/{atendimentoId}/exit-inspection")
        {
            Content = JsonContent.Create(new { texto = "Texto.", versaoAtendimentoEsperada = "AAAA" })
        };
        vistoriaAnonima.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var semSessaoVistoria = await anonimo.SendAsync(vistoriaAnonima);
        Assert.AreEqual(HttpStatusCode.Unauthorized, semSessaoVistoria.StatusCode);

        using var corsPermitido = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/withdrawals/{pedidoId}/delivery-readiness");
        corsPermitido.Headers.Add("Origin", "https://localhost:4200");
        using var respostaPermitida = await caixa.SendAsync(corsPermitido);
        Assert.AreEqual(HttpStatusCode.OK, respostaPermitida.StatusCode);
        Assert.AreEqual(
            "https://localhost:4200",
            respostaPermitida.Headers.GetValues("Access-Control-Allow-Origin").Single());

        using var corsNegado = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/withdrawals/{pedidoId}/delivery-readiness");
        corsNegado.Headers.Add("Origin", "https://evil.test");
        using var respostaNegada = await caixa.SendAsync(corsNegado);
        Assert.IsFalse(respostaNegada.Headers.Contains("Access-Control-Allow-Origin"));

        using var manobristaProntidao = await manobrista.GetAsync(
            $"/api/v1/withdrawals/{pedidoId}/delivery-readiness");
        Assert.AreEqual(HttpStatusCode.Forbidden, manobristaProntidao.StatusCode);

        using var logout = await caixa.PostAsync("/api/v1/session/logout", null);
        Assert.AreEqual(HttpStatusCode.NoContent, logout.StatusCode);

        using var sessaoRevogada = await caixa.GetAsync(
            $"/api/v1/withdrawals/{pedidoId}/delivery-readiness");
        Assert.AreEqual(HttpStatusCode.Unauthorized, sessaoRevogada.StatusCode);
    }

    [TestMethod]
    public async Task ErrosDasNovasRotasUsamProblemDetailsComCorrelacaoESemDadosSensiveis()
    {
        using var fabrica = Fase4Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);
        var (manobrista, _) = await Fase2Apoio.EntrarManobristaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa);
        var pedidoId = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, atendimentoId);

        using var semChave = await caixa.PostAsJsonAsync(
            $"/api/v1/attendances/{atendimentoId}/exit-inspection",
            new { texto = "Texto.", versaoAtendimentoEsperada = "AAAAAAA=" });
        Assert.AreEqual(HttpStatusCode.BadRequest, semChave.StatusCode);
        Assert.AreEqual("idempotency_key_invalida", await Fase4Apoio.CodigoAsync(semChave));
        Assert.AreEqual(
            "application/problem+json",
            semChave.Content.Headers.ContentType?.MediaType);

        using var documento = JsonDocument.Parse(await semChave.Content.ReadAsStringAsync());
        Assert.AreEqual(
            semChave.Headers.GetValues("X-Correlation-Id").Single(),
            documento.RootElement.GetProperty("correlationId").GetString());

        var corpo = await semChave.Content.ReadAsStringAsync();
        Assert.IsFalse(corpo.Contains("ABC1D23", StringComparison.Ordinal));
        Assert.IsFalse(corpo.Contains("Titular Sintético", StringComparison.Ordinal));
        Assert.IsFalse(corpo.Contains(ContasTeste.Senha, StringComparison.Ordinal));

        using var leituraSemVistoria = await manobrista.GetAsync(
            $"/api/v1/attendances/{atendimentoId}/exit-inspection");
        Assert.AreEqual(HttpStatusCode.NotFound, leituraSemVistoria.StatusCode);
        Assert.AreEqual("exit_inspection_not_found", await Fase4Apoio.CodigoAsync(leituraSemVistoria));

        await Fase4Apoio.RegistrarVistoriaConfirmadaAsync(manobrista, atendimentoId);
        using var prontidao = await Fase4Apoio.ProntidaoAsync(caixa, pedidoId);
        var corpoProntidao = await caixa.GetStringAsync(
            $"/api/v1/withdrawals/{pedidoId}/delivery-readiness");
        Assert.IsFalse(corpoProntidao.Contains("Condições de saída sintéticas", StringComparison.Ordinal));
        Assert.IsFalse(corpoProntidao.Contains("Titular Sintético", StringComparison.Ordinal));

        using var erroNegocio = await Fase4Apoio.ConfirmarEntregaAsync(caixa, pedidoId, prontidao);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, erroNegocio.StatusCode);
        Assert.AreEqual("delivery_balance_outstanding", await Fase4Apoio.CodigoAsync(erroNegocio));
        var corpoErro = await erroNegocio.Content.ReadAsStringAsync();
        Assert.IsFalse(corpoErro.Contains("ABC1D23", StringComparison.Ordinal));
        Assert.IsFalse(corpoErro.Contains("R$", StringComparison.Ordinal));
        Assert.IsFalse(corpoErro.Contains("Condições de saída", StringComparison.Ordinal));
    }
}
