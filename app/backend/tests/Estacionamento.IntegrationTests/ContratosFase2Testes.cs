using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Estacionamento.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Estacionamento.IntegrationTests;

[TestClass]
public sealed class ContratosFase2Testes
{
    [TestMethod]
    public async Task ErrosUsamProblemDetailsComCodigoCorrelacaoESemPii()
    {
        using var fabrica = Fase2Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        await Fase2Apoio.PublicarTarifaAsync(administrador, 20.00m);

        var conflito = await administrador.PostAsJsonAsync("/api/v1/daily-rates", new
        {
            valor = 30.00m,
            versaoBaseEsperada = (int?)null
        });

        Assert.AreEqual(HttpStatusCode.Conflict, conflito.StatusCode);
        Assert.AreEqual(
            "application/problem+json",
            conflito.Content.Headers.ContentType?.MediaType);
        Assert.AreEqual("no-store", conflito.Headers.CacheControl?.ToString().Split(',').First().Trim());

        var corpo = await conflito.Content.ReadAsStringAsync();
        Assert.IsFalse(corpo.Contains(Fase2Apoio.EmailAdministrador, StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(corpo.Contains(ContasTeste.Senha, StringComparison.Ordinal));

        using var json = JsonDocument.Parse(corpo);
        Assert.AreEqual("stale_rate_version", json.RootElement.GetProperty("code").GetString());
        var correlacaoCorpo = json.RootElement.GetProperty("correlationId").GetString();
        var correlacaoCabecalho = conflito.Headers.GetValues("X-Correlation-Id").Single();
        Assert.IsFalse(string.IsNullOrWhiteSpace(correlacaoCorpo));
        Assert.AreEqual(correlacaoCabecalho, correlacaoCorpo);

        using var vaga = await Fase2Apoio.CriarVagaAsync(administrador, "CONTRATO-01");
        var vagaId = vaga.RootElement.GetProperty("id").GetGuid();
        var desconhecida = await Fase2Apoio.RegistrarEntradaAsync(
            caixa,
            vagaId,
            Guid.NewGuid(),
            extras: new { tarifa = 10.00m });

        Assert.AreEqual(HttpStatusCode.BadRequest, desconhecida.StatusCode);
        var corpoInvalido = await desconhecida.Content.ReadAsStringAsync();
        Assert.IsFalse(corpoInvalido.Contains("000.000.000-00", StringComparison.Ordinal));
        Assert.IsFalse(corpoInvalido.Contains(ContasTeste.Senha, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task OpenApiEmDevelopmentDocumentaOsContratosDaFase2SemAlterarContas()
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
            "/api/v1/employees",
            "/api/v1/employees/{id}/access",
            "/api/v1/spaces",
            "/api/v1/daily-rates",
            "/api/v1/daily-rates/current",
            "/api/v1/attendances",
            "/api/v1/attendances/{id}",
            "/api/v1/attendances/{id}/history",
            "/api/v1/attendances/{id}/document",
            "/api/v1/attendances/{id}/entry-inspection"
        ];

        foreach (var caminho in esperados)
        {
            Assert.IsTrue(
                caminhos.TryGetProperty(caminho, out _),
                $"O OpenAPI deve documentar {caminho}.");
        }

        using (var escopo = fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
            Assert.AreEqual(contasAntes, await db.Users.CountAsync());
        }
    }
}
