using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Estacionamento.Domain.Autenticacao;

namespace Estacionamento.IntegrationTests;

[TestClass]
public sealed class VagasTarifasTestes
{
    [TestMethod]
    public async Task VagaEhCriadaComCodigoNormalizadoEListadaParaOsTresPerfis()
    {
        using var fabrica = Fase2Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);
        var (manobrista, _) = await Fase2Apoio.EntrarManobristaAsync(fabrica);

        using var vaga = await Fase2Apoio.CriarVagaAsync(administrador, "  a-01  ");
        Assert.AreEqual("A-01", vaga.RootElement.GetProperty("codigo").GetString());
        Assert.AreEqual("livre", vaga.RootElement.GetProperty("situacao").GetString());

        foreach (var cliente in new[] { administrador, caixa, manobrista })
        {
            var resposta = await cliente.GetAsync("/api/v1/spaces?code=a-01&page=1&pageSize=20");
            Assert.AreEqual(HttpStatusCode.OK, resposta.StatusCode);
            using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
            Assert.AreEqual(1, json.RootElement.GetProperty("total").GetInt32());
        }

        Assert.AreEqual(
            HttpStatusCode.Forbidden,
            (await caixa.PostAsJsonAsync("/api/v1/spaces", new { codigo = "B-01" })).StatusCode);
        Assert.AreEqual(
            HttpStatusCode.Forbidden,
            (await manobrista.PostAsJsonAsync("/api/v1/spaces", new { codigo = "B-02" })).StatusCode);
    }

    [TestMethod]
    public async Task CodigoDuplicadoOuInvalidoNaoCriaVagaParcial()
    {
        using var fabrica = Fase2Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);

        await Fase2Apoio.CriarVagaAsync(administrador, "A-01");

        var duplicada = await administrador.PostAsJsonAsync("/api/v1/spaces", new { codigo = " a-01 " });
        Assert.AreEqual(HttpStatusCode.Conflict, duplicada.StatusCode);
        Assert.AreEqual("space_code_conflict", await Fase2Apoio.CodigoDoErroAsync(duplicada));

        var vazia = await administrador.PostAsJsonAsync("/api/v1/spaces", new { codigo = "   " });
        Assert.AreEqual(HttpStatusCode.BadRequest, vazia.StatusCode);

        var longa = await administrador.PostAsJsonAsync(
            "/api/v1/spaces",
            new { codigo = new string('X', 33) });
        Assert.AreEqual(HttpStatusCode.BadRequest, longa.StatusCode);

        var listagem = await administrador.GetAsync("/api/v1/spaces?page=1&pageSize=20");
        using var json = JsonDocument.Parse(await listagem.Content.ReadAsStringAsync());
        Assert.AreEqual(1, json.RootElement.GetProperty("total").GetInt32());
    }

    [TestMethod]
    public async Task PaginacaoInvalidaEhRecusadaSemAjusteSilencioso()
    {
        using var fabrica = Fase2Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);

        var paginaZero = await administrador.GetAsync("/api/v1/spaces?page=0");
        Assert.AreEqual(HttpStatusCode.BadRequest, paginaZero.StatusCode);
        Assert.AreEqual("paginacao_invalida", await Fase2Apoio.CodigoDoErroAsync(paginaZero));

        var tamanhoExcessivo = await administrador.GetAsync("/api/v1/spaces?pageSize=101");
        Assert.AreEqual(HttpStatusCode.BadRequest, tamanhoExcessivo.StatusCode);
        Assert.AreEqual("paginacao_invalida", await Fase2Apoio.CodigoDoErroAsync(tamanhoExcessivo));
    }

    [TestMethod]
    public async Task SituacaoDaVagaEhDerivadaDaOcupacaoAberta()
    {
        using var fabrica = Fase2Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        var vagaId = await Fase2Apoio.PrepararEntradaAsync(administrador);
        var entrada = await Fase2Apoio.RegistrarEntradaAsync(caixa, vagaId, Guid.NewGuid());
        Assert.AreEqual(HttpStatusCode.Created, entrada.StatusCode);

        var ocupadas = await administrador.GetAsync("/api/v1/spaces?status=ocupada");
        using var jsonOcupadas = JsonDocument.Parse(await ocupadas.Content.ReadAsStringAsync());
        Assert.AreEqual(1, jsonOcupadas.RootElement.GetProperty("total").GetInt32());

        var livres = await administrador.GetAsync("/api/v1/spaces?status=livre");
        using var jsonLivres = JsonDocument.Parse(await livres.Content.ReadAsStringAsync());
        Assert.AreEqual(0, jsonLivres.RootElement.GetProperty("total").GetInt32());
    }

    [TestMethod]
    public async Task TarifaSemValorInicialEhPublicadaComConcorrenciaPorVersaoBase()
    {
        using var fabrica = Fase2Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        var semTarifa = await administrador.GetAsync("/api/v1/daily-rates/current");
        using (var jsonSem = JsonDocument.Parse(await semTarifa.Content.ReadAsStringAsync()))
        {
            Assert.IsFalse(jsonSem.RootElement.GetProperty("temTarifa").GetBoolean());
            Assert.IsNull(jsonSem.RootElement.GetProperty("valor").GetDecimalNullable());
            Assert.AreEqual(JsonValueKind.Null, jsonSem.RootElement.GetProperty("versaoBase").ValueKind);
        }

        await Fase2Apoio.PublicarTarifaAsync(administrador, 20.00m);

        var corrente = await administrador.GetAsync("/api/v1/daily-rates/current");
        using var jsonCorrente = JsonDocument.Parse(await corrente.Content.ReadAsStringAsync());
        Assert.IsTrue(jsonCorrente.RootElement.GetProperty("temTarifa").GetBoolean());
        Assert.AreEqual(20.00m, jsonCorrente.RootElement.GetProperty("valor").GetDecimal());
        Assert.AreEqual(1, jsonCorrente.RootElement.GetProperty("versaoBase").GetInt32());

        // Duas publicações a partir da mesma base: apenas uma é confirmada.
        var primeira = administrador.PostAsJsonAsync("/api/v1/daily-rates", new
        {
            valor = 25.00m,
            versaoBaseEsperada = 1
        });
        var segunda = administrador.PostAsJsonAsync("/api/v1/daily-rates", new
        {
            valor = 30.00m,
            versaoBaseEsperada = 1
        });

        var respostas = await Task.WhenAll(primeira, segunda);
        Assert.AreEqual(1, respostas.Count(resposta => resposta.StatusCode == HttpStatusCode.Created));
        Assert.AreEqual(1, respostas.Count(resposta => resposta.StatusCode == HttpStatusCode.Conflict));

        var conflito = respostas.Single(resposta => resposta.StatusCode == HttpStatusCode.Conflict);
        Assert.AreEqual("stale_rate_version", await Fase2Apoio.CodigoDoErroAsync(conflito));

        var historico = await administrador.GetAsync("/api/v1/daily-rates?page=1&pageSize=20");
        using var jsonHistorico = JsonDocument.Parse(await historico.Content.ReadAsStringAsync());
        Assert.AreEqual(2, jsonHistorico.RootElement.GetProperty("total").GetInt32());
        Assert.AreEqual(
            2,
            jsonHistorico.RootElement.GetProperty("items")[0].GetProperty("versao").GetInt32());

        Assert.AreEqual(
            HttpStatusCode.Forbidden,
            (await caixa.PostAsJsonAsync("/api/v1/daily-rates", new
            {
                valor = 10.00m,
                versaoBaseEsperada = 2
            })).StatusCode);

        var invalida = await administrador.PostAsJsonAsync("/api/v1/daily-rates", new
        {
            valor = 10.001m,
            versaoBaseEsperada = 2
        });
        Assert.AreEqual(HttpStatusCode.BadRequest, invalida.StatusCode);
        Assert.AreEqual("valor_tarifa_invalido", await Fase2Apoio.CodigoDoErroAsync(invalida));

        var zero = await administrador.PostAsJsonAsync("/api/v1/daily-rates", new
        {
            valor = 0m,
            versaoBaseEsperada = 2
        });
        Assert.AreEqual(HttpStatusCode.BadRequest, zero.StatusCode);
    }
}

internal static class JsonElementExtensoes
{
    public static decimal? GetDecimalNullable(this JsonElement elemento) =>
        elemento.ValueKind == JsonValueKind.Null ? null : elemento.GetDecimal();
}
