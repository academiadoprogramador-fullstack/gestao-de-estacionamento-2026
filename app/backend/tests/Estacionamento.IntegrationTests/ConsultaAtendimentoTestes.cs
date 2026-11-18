using System.Net;
using System.Text.Json;
using Estacionamento.Domain.Atendimentos;
using Estacionamento.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Estacionamento.IntegrationTests;

[TestClass]
public sealed class ConsultaAtendimentoTestes
{
    [TestMethod]
    public async Task BuscaExigeExatamenteUmFiltroENaoListaTudoPorOmissao()
    {
        using var fabrica = Fase2Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        var vagaId = await Fase2Apoio.PrepararEntradaAsync(administrador);
        var entrada = await Fase2Apoio.RegistrarEntradaAsync(caixa, vagaId, Guid.NewGuid());
        Assert.AreEqual(HttpStatusCode.Created, entrada.StatusCode);

        Assert.AreEqual(
            HttpStatusCode.BadRequest,
            (await caixa.GetAsync("/api/v1/attendances")).StatusCode);

        Assert.AreEqual(
            HttpStatusCode.BadRequest,
            (await caixa.GetAsync("/api/v1/attendances?plate=ABC1D23&identifier=AT-2026-000001")).StatusCode);

        var invalido = await caixa.GetAsync("/api/v1/attendances?identifier=xyz");
        Assert.AreEqual(HttpStatusCode.BadRequest, invalido.StatusCode);
        Assert.AreEqual("identificador_invalido", await Fase2Apoio.CodigoDoErroAsync(invalido));

        var vazio = await caixa.GetAsync("/api/v1/attendances?plate=ZZZ9Z99");
        Assert.AreEqual(HttpStatusCode.OK, vazio.StatusCode);
        using var jsonVazio = JsonDocument.Parse(await vazio.Content.ReadAsStringAsync());
        Assert.AreEqual(0, jsonVazio.RootElement.GetProperty("total").GetInt32());
        Assert.AreEqual(0, jsonVazio.RootElement.GetProperty("items").GetArrayLength());
    }

    [TestMethod]
    public async Task BuscaPorPlacaNormalizadaEDistintasVisitasDoMesmoVeiculo()
    {
        using var fabrica = Fase2Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        using var vaga1 = await Fase2Apoio.CriarVagaAsync(administrador, "A-01");
        using var vaga2 = await Fase2Apoio.CriarVagaAsync(administrador, "A-02");
        await Fase2Apoio.PublicarTarifaAsync(administrador, 20.00m);

        foreach (var vaga in new[] { vaga1, vaga2 })
        {
            var resposta = await Fase2Apoio.RegistrarEntradaAsync(
                caixa,
                vaga.RootElement.GetProperty("id").GetGuid(),
                Guid.NewGuid(),
                placa: "abc-1d2 3");
            Assert.AreEqual(HttpStatusCode.Created, resposta.StatusCode);
        }

        var busca = await caixa.GetAsync("/api/v1/attendances?plate=ABC1D23&page=1&pageSize=20");
        Assert.AreEqual(HttpStatusCode.OK, busca.StatusCode);

        using var json = JsonDocument.Parse(await busca.Content.ReadAsStringAsync());
        Assert.AreEqual(2, json.RootElement.GetProperty("total").GetInt32());
        var itens = json.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.AreEqual(2, itens.Select(item => item.GetProperty("id").GetGuid()).Distinct().Count());
        Assert.IsTrue(itens.All(item => item.GetProperty("placa").GetString() == "ABC1D23"));
        Assert.IsFalse(itens.All(item => item.TryGetProperty("documentoTitularCifrado", out _)));
        Assert.IsFalse(itens.All(item => item.TryGetProperty("vistoria", out _)));
    }

    [TestMethod]
    public async Task ProjecoesPorPerfilOmitemFinancasDoManobrista()
    {
        using var fabrica = Fase2Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);
        var (manobrista, _) = await Fase2Apoio.EntrarManobristaAsync(fabrica);

        var vagaId = await Fase2Apoio.PrepararEntradaAsync(administrador);
        var entrada = await Fase2Apoio.RegistrarEntradaAsync(caixa, vagaId, Guid.NewGuid());
        using var jsonEntrada = await Fase2Apoio.LerJsonAsync(entrada);
        var atendimentoId = jsonEntrada.RootElement.GetProperty("atendimentoId").GetGuid();
        var identificador = jsonEntrada.RootElement.GetProperty("identificador").GetString();

        var buscaIdentificador = await manobrista.GetAsync(
            $"/api/v1/attendances?identifier={identificador}");
        Assert.AreEqual(HttpStatusCode.OK, buscaIdentificador.StatusCode);

        using var jsonLista = JsonDocument.Parse(await buscaIdentificador.Content.ReadAsStringAsync());
        var item = jsonLista.RootElement.GetProperty("items")[0];
        Assert.IsFalse(item.TryGetProperty("documentoTitularCifrado", out _));
        Assert.IsFalse(item.TryGetProperty("vistoria", out _));

        var detalheManobrista = await manobrista.GetAsync($"/api/v1/attendances/{atendimentoId}");
        Assert.AreEqual(HttpStatusCode.OK, detalheManobrista.StatusCode);
        var corpoManobrista = await detalheManobrista.Content.ReadAsStringAsync();
        Assert.IsFalse(corpoManobrista.Contains("tarifaVersao", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(corpoManobrista.Contains("tarifaValor", StringComparison.OrdinalIgnoreCase));

        var detalheCaixa = await caixa.GetAsync($"/api/v1/attendances/{atendimentoId}");
        Assert.AreEqual(HttpStatusCode.OK, detalheCaixa.StatusCode);
        using var jsonCaixa = JsonDocument.Parse(await detalheCaixa.Content.ReadAsStringAsync());
        Assert.AreEqual(20.00m, jsonCaixa.RootElement.GetProperty("tarifaValorSnapshot").GetDecimal());
        Assert.AreEqual(1, jsonCaixa.RootElement.GetProperty("tarifaVersao").GetInt32());
    }

    [TestMethod]
    public async Task LeituraDeDocumentoEVistoriaEhAuditadaAntesDoConteudo()
    {
        using var fabrica = Fase2Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);
        var (manobrista, _) = await Fase2Apoio.EntrarManobristaAsync(fabrica);

        var vagaId = await Fase2Apoio.PrepararEntradaAsync(administrador);
        var entrada = await Fase2Apoio.RegistrarEntradaAsync(
            caixa,
            vagaId,
            Guid.NewGuid(),
            documento: "111.222.333-44",
            vistoria: "Vistoria confidencial de entrada.");
        using var jsonEntrada = await Fase2Apoio.LerJsonAsync(entrada);
        var atendimentoId = jsonEntrada.RootElement.GetProperty("atendimentoId").GetGuid();

        var negado = await manobrista.GetAsync($"/api/v1/attendances/{atendimentoId}/document");
        Assert.AreEqual(HttpStatusCode.Forbidden, negado.StatusCode);

        var documento = await caixa.GetAsync($"/api/v1/attendances/{atendimentoId}/document");
        Assert.AreEqual(HttpStatusCode.OK, documento.StatusCode);
        using var jsonDocumento = JsonDocument.Parse(await documento.Content.ReadAsStringAsync());
        Assert.AreEqual("111.222.333-44", jsonDocumento.RootElement.GetProperty("conteudo").GetString());

        var vistoria = await manobrista.GetAsync($"/api/v1/attendances/{atendimentoId}/entry-inspection");
        Assert.AreEqual(HttpStatusCode.OK, vistoria.StatusCode);
        using var jsonVistoria = JsonDocument.Parse(await vistoria.Content.ReadAsStringAsync());
        Assert.AreEqual(
            "Vistoria confidencial de entrada.",
            jsonVistoria.RootElement.GetProperty("conteudo").GetString());

        using (var escopo = fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
            var auditorias = await db.EventosAuditoriaNegocio.AsNoTracking().ToListAsync();
            Assert.IsTrue(auditorias.Any(evento => evento.Tipo == TiposAuditoriaNegocio.DocumentoLido));
            Assert.IsTrue(auditorias.Any(evento => evento.Tipo == TiposAuditoriaNegocio.VistoriaLida));
            Assert.IsFalse(auditorias.Any(evento =>
                (evento.Detalhe ?? string.Empty).Contains("111.222.333-44", StringComparison.Ordinal)));
        }

        var historicoManobrista = await manobrista.GetAsync(
            $"/api/v1/attendances/{atendimentoId}/history?page=1&pageSize=20");
        using var jsonHistoricoManobrista = JsonDocument.Parse(
            await historicoManobrista.Content.ReadAsStringAsync());
        var tiposManobrista = jsonHistoricoManobrista.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("tipo").GetString())
            .ToArray();
        CollectionAssert.Contains(tiposManobrista, TiposEventoAtendimento.VistoriaEntradaLida);
        CollectionAssert.DoesNotContain(tiposManobrista, TiposEventoAtendimento.DocumentoLido);

        var historicoCaixa = await caixa.GetAsync(
            $"/api/v1/attendances/{atendimentoId}/history?page=1&pageSize=20");
        using var jsonHistoricoCaixa = JsonDocument.Parse(
            await historicoCaixa.Content.ReadAsStringAsync());
        var tiposCaixa = jsonHistoricoCaixa.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("tipo").GetString())
            .ToArray();
        CollectionAssert.Contains(tiposCaixa, TiposEventoAtendimento.DocumentoLido);
    }

    [TestMethod]
    public async Task FalhaDeDecifragemRetornaErroControladoSemConteudo()
    {
        using var fabrica = Fase2Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        var vagaId = await Fase2Apoio.PrepararEntradaAsync(administrador);
        var entrada = await Fase2Apoio.RegistrarEntradaAsync(
            caixa,
            vagaId,
            Guid.NewGuid(),
            documento: "999.888.777-66");
        using var jsonEntrada = await Fase2Apoio.LerJsonAsync(entrada);
        var atendimentoId = jsonEntrada.RootElement.GetProperty("atendimentoId").GetGuid();

        using (var escopo = fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
            var atendimento = await db.Atendimentos.SingleAsync();
            var alterado = atendimento.DocumentoTitularCifrado.ToArray();
            alterado[^1] ^= 0xFF;
            atendimento.DocumentoTitularCifrado = alterado;
            db.Entry(atendimento).Property(registro => registro.DocumentoTitularCifrado).IsModified = true;
            await db.SaveChangesAsync();
        }

        var leitura = await caixa.GetAsync($"/api/v1/attendances/{atendimentoId}/document");
        Assert.AreEqual(HttpStatusCode.InternalServerError, leitura.StatusCode);

        var corpo = await leitura.Content.ReadAsStringAsync();
        Assert.AreEqual("field_protection_failure", await Fase2Apoio.CodigoDoErroAsync(leitura));
        Assert.IsFalse(corpo.Contains("999.888.777-66", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task RotacaoDeChaveAesMantemLeituraDeEnvelopeAnterior()
    {
        var chaveV1 = Convert.ToBase64String(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var chaveV2 = Convert.ToBase64String(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        string nomeBanco;
        Guid atendimentoAntigo;

        using (var primeira = new FabricaApiTeste(
            configuracaoExtra: new Dictionary<string, string?>
            {
                ["Autenticacao:LimiteTentativasLoginPorMinuto"] = "100",
                ["ProtecaoDados:VersaoAtiva"] = "v1",
                ["ProtecaoDados:Chaves:v1"] = chaveV1
            },
            removerBancoAoDispor: false))
        {
            nomeBanco = primeira.NomeBanco;
            await primeira.ProvisionarContasAsync();
            var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(primeira);
            var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(primeira);

            var vagaId = await Fase2Apoio.PrepararEntradaAsync(administrador);
            var entrada = await Fase2Apoio.RegistrarEntradaAsync(
                caixa,
                vagaId,
                Guid.NewGuid(),
                documento: "documento-rotacao");
            using var json = await Fase2Apoio.LerJsonAsync(entrada);
            atendimentoAntigo = json.RootElement.GetProperty("atendimentoId").GetGuid();
        }

        try
        {
            using var segunda = new FabricaApiTeste(
                configuracaoExtra: new Dictionary<string, string?>
                {
                    ["Autenticacao:LimiteTentativasLoginPorMinuto"] = "100",
                    ["ProtecaoDados:VersaoAtiva"] = "v2",
                    ["ProtecaoDados:Chaves:v1"] = chaveV1,
                    ["ProtecaoDados:Chaves:v2"] = chaveV2
                },
                nomeBancoExistente: nomeBanco,
                removerBancoAoDispor: false);

            var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(segunda);
            var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(segunda);

            var leitura = await caixa.GetAsync($"/api/v1/attendances/{atendimentoAntigo}/document");
            Assert.AreEqual(HttpStatusCode.OK, leitura.StatusCode, await leitura.Content.ReadAsStringAsync());
            using var json = JsonDocument.Parse(await leitura.Content.ReadAsStringAsync());
            Assert.AreEqual("documento-rotacao", json.RootElement.GetProperty("conteudo").GetString());

            using var vaga = await Fase2Apoio.CriarVagaAsync(administrador, "ROT-01");
            var entradaNova = await Fase2Apoio.RegistrarEntradaAsync(
                caixa,
                vaga.RootElement.GetProperty("id").GetGuid(),
                Guid.NewGuid(),
                placa: "ROT1A23",
                documento: "documento-novo");
            Assert.AreEqual(HttpStatusCode.Created, entradaNova.StatusCode);

            using var escopo = segunda.Services.CreateScope();
            var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
            var novo = await db.Atendimentos
                .AsNoTracking()
                .OrderByDescending(atendimento => atendimento.EntradaUtc)
                .FirstAsync();
            Assert.AreEqual("v2", System.Text.Encoding.UTF8.GetString(
                novo.DocumentoTitularCifrado,
                2,
                novo.DocumentoTitularCifrado[1]));

            var antigo = await db.Atendimentos.AsNoTracking().SingleAsync(atendimento => atendimento.Id == atendimentoAntigo);
            Assert.AreEqual("v1", System.Text.Encoding.UTF8.GetString(
                antigo.DocumentoTitularCifrado,
                2,
                antigo.DocumentoTitularCifrado[1]));
        }
        finally
        {
            SqlTeste.RemoverBanco(nomeBanco);
        }
    }
}
