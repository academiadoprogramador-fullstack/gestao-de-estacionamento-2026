using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Estacionamento.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Estacionamento.IntegrationTests;

[TestClass]
public sealed class ContratosFase3Testes
{
    [TestMethod]
    public async Task ErrosDaFase3UsamProblemDetailsComCodigoCorrelacaoESemPii()
    {
        using var fabrica = Fase3Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa);
        await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, atendimentoId);

        using var semChave = await caixa.PostAsJsonAsync(
            $"/api/v1/attendances/{atendimentoId}/withdrawal",
            new { titularPresente = true, conferencia = "aprovada" });

        Assert.AreEqual(HttpStatusCode.BadRequest, semChave.StatusCode);
        Assert.AreEqual(
            "application/problem+json",
            semChave.Content.Headers.ContentType?.MediaType);
        Assert.AreEqual(
            "no-store",
            semChave.Headers.CacheControl?.ToString().Split(',').First().Trim());
        Assert.AreEqual("idempotency_key_invalida", await Fase3Apoio.CodigoAsync(semChave));

        using var documento = JsonDocument.Parse(await semChave.Content.ReadAsStringAsync());
        var correlacaoCorpo = documento.RootElement.GetProperty("correlationId").GetString();
        var correlacaoCabecalho = semChave.Headers.GetValues("X-Correlation-Id").Single();
        Assert.AreEqual(correlacaoCabecalho, correlacaoCorpo);

        var corpo = await semChave.Content.ReadAsStringAsync();
        Assert.IsFalse(corpo.Contains(Fase2Apoio.EmailCaixa, StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(corpo.Contains("ABC1D23", StringComparison.Ordinal));
        Assert.IsFalse(corpo.Contains(ContasTeste.Senha, StringComparison.Ordinal));

        using var propriedadeInesperada = await Fase3Apoio.RegistrarPedidoAsync(
            caixa,
            atendimentoId,
            extras: new { tarifa = 10.00m, horario = "2026-01-01T00:00:00Z" });
        Assert.AreEqual(HttpStatusCode.BadRequest, propriedadeInesperada.StatusCode);

        var corpoInvalido = await propriedadeInesperada.Content.ReadAsStringAsync();
        Assert.IsFalse(corpoInvalido.Contains("000.000.000-00", StringComparison.Ordinal));
        Assert.IsFalse(corpoInvalido.Contains(ContasTeste.Senha, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task OpenApiDocumentaOsContratosDaFase3SemRotasDeEntregaNemBootstrap()
    {
        using var fabrica = new FabricaApiTeste("Development", aplicarMigrationsAntesDoStartup: false);
        using var cliente = fabrica.CriarCliente();

        int contasAntes;
        using (var escopo = fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
            contasAntes = await db.Users.CountAsync();
        }

        var login = await ClienteSessao.LoginAdministradorAsync(cliente);
        ClienteSessao.UsarBearer(cliente, login.TokenAcesso);

        var resposta = await cliente.GetAsync("/openapi/v1.json");
        Assert.AreEqual(HttpStatusCode.OK, resposta.StatusCode);
        Assert.AreEqual("no-store", resposta.Headers.CacheControl?.ToString().Split(',').First().Trim());

        using var documento = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        var caminhos = documento.RootElement.GetProperty("paths");
        string[] esperados =
        [
            "/api/v1/attendances/{id}/withdrawal",
            "/api/v1/withdrawals",
            "/api/v1/withdrawals/{id}",
            "/api/v1/withdrawals/{id}/search",
            "/api/v1/withdrawals/{id}/cashier",
            "/api/v1/withdrawals/{id}/payments",
            "/api/v1/withdrawals/{id}/discounts",
            "/api/v1/withdrawals/{id}/refunds",
            "/api/v1/withdrawals/{id}/authorizations",
            "/api/v1/withdrawals/{id}/authorizations/{authorizationId}/revoke",
            "/api/v1/withdrawals/{id}/authorizations/{authorizationId}/justification",
            "/api/v1/withdrawals/{id}/launches/{launchId}/justification",
            "/api/v1/attendances/{id}/exit-inspection",
            "/api/v1/withdrawals/{id}/delivery-readiness",
            "/api/v1/withdrawals/{id}/delivery"
        ];

        foreach (var caminho in esperados)
        {
            Assert.IsTrue(
                caminhos.TryGetProperty(caminho, out _),
                $"O OpenAPI deve documentar {caminho}.");
        }

        string[] codigosEntrega = ["201", "400", "403", "404", "409", "422"];
        var respostasEntrega = caminhos
            .GetProperty("/api/v1/withdrawals/{id}/delivery")
            .GetProperty("post")
            .GetProperty("responses");
        foreach (var codigo in codigosEntrega)
        {
            Assert.IsTrue(
                respostasEntrega.TryGetProperty(codigo, out _),
                $"O OpenAPI deve documentar o status {codigo} da entrega.");
        }

        var respostasVistoria = caminhos
            .GetProperty("/api/v1/attendances/{id}/exit-inspection")
            .GetProperty("post")
            .GetProperty("responses");
        foreach (var codigo in new[] { "201", "400", "403", "404", "409", "422" })
        {
            Assert.IsTrue(
                respostasVistoria.TryGetProperty(codigo, out _),
                $"O OpenAPI deve documentar o status {codigo} da vistoria de saída.");
        }

        string[] proibidos =
        [
            "/api/v1/withdrawals/{id}/authorizations/{authorizationId}/consume"
        ];

        foreach (var caminho in proibidos)
        {
            Assert.IsFalse(
                caminhos.TryGetProperty(caminho, out _),
                $"A Fase 3 não deve documentar {caminho}.");
        }

        using (var escopo = fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
            Assert.AreEqual(contasAntes, await db.Users.CountAsync());
        }
    }
}
