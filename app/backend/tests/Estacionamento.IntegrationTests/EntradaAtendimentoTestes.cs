using System.Net;
using System.Text;
using System.Text.Json;
using Estacionamento.Domain.Atendimentos;
using Estacionamento.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Estacionamento.IntegrationTests;

[TestClass]
public sealed class EntradaAtendimentoTestes
{
    [TestMethod]
    public async Task EntradaCompletaPersisteSnapshotCifradoEEhIdempotente()
    {
        using var fabrica = Fase2Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        var vagaId = await Fase2Apoio.PrepararEntradaAsync(administrador);
        var chave = Guid.NewGuid();

        var resposta = await Fase2Apoio.RegistrarEntradaAsync(caixa, vagaId, chave);
        Assert.AreEqual(HttpStatusCode.Created, resposta.StatusCode, await resposta.Content.ReadAsStringAsync());
        Assert.IsNotNull(resposta.Headers.Location);
        Assert.IsTrue(resposta.Headers.Location!.ToString().StartsWith("/api/v1/attendances/"));

        using var json = await Fase2Apoio.LerJsonAsync(resposta);
        var identificador = json.RootElement.GetProperty("identificador").GetString();
        var atendimentoId = json.RootElement.GetProperty("atendimentoId").GetGuid();
        Assert.IsTrue(IdentificadorAtendimento.EhValido(identificador));
        Assert.AreEqual("A-01", json.RootElement.GetProperty("vagaCodigo").GetString());
        Assert.AreEqual(EstadoAtendimento.Ativo, json.RootElement.GetProperty("estado").GetString());

        using (var escopo = fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
            var atendimento = await db.Atendimentos.AsNoTracking().SingleAsync();
            var vistoria = await db.VistoriasEntrada.AsNoTracking().SingleAsync();
            var ocupacao = await db.Ocupacoes.AsNoTracking().SingleAsync();
            var resultados = await db.ResultadosIdempotentes.AsNoTracking().ToListAsync();
            var eventos = await db.EventosAtendimento.AsNoTracking().ToListAsync();

            Assert.AreEqual(20.00m, atendimento.TarifaValorSnapshot);
            Assert.AreNotEqual(Guid.Empty, atendimento.TarifaVersaoId);
            Assert.AreEqual(atendimentoId, atendimento.Id);
            Assert.AreEqual(vagaId, ocupacao.VagaId);
            Assert.IsNull(ocupacao.FimUtc);
            Assert.AreEqual(1, resultados.Count);
            Assert.AreEqual(identificador, resultados[0].IdentificadorPublico);
            Assert.AreEqual(2, eventos.Count);

            var documentoPuro = Encoding.UTF8.GetBytes("000.000.000-00");
            var vistoriaPura = Encoding.UTF8.GetBytes("Vistoria de entrada sintética.");
            Assert.IsFalse(Contem(atendimento.DocumentoTitularCifrado, documentoPuro));
            Assert.IsFalse(Contem(vistoria.TextoCifrado, vistoriaPura));
            Assert.IsTrue(atendimento.DocumentoTitularCifrado.Length > 0);
        }

        // Replay idêntico: mesmo resultado e nenhum efeito duplicado.
        var replay = await Fase2Apoio.RegistrarEntradaAsync(caixa, vagaId, chave);
        Assert.AreEqual(HttpStatusCode.Created, replay.StatusCode, await replay.Content.ReadAsStringAsync());
        using (var jsonReplay = await Fase2Apoio.LerJsonAsync(replay))
        {
            Assert.AreEqual(identificador, jsonReplay.RootElement.GetProperty("identificador").GetString());
            Assert.AreEqual(atendimentoId, jsonReplay.RootElement.GetProperty("atendimentoId").GetGuid());
        }

        using (var escopo = fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
            Assert.AreEqual(1, await db.Atendimentos.CountAsync());
            Assert.AreEqual(1, await db.Ocupacoes.CountAsync());
            Assert.AreEqual(1, await db.VistoriasEntrada.CountAsync());
            Assert.AreEqual(1, await db.ResultadosIdempotentes.CountAsync());
        }

        // Mesma chave com conteúdo divergente conflita sem alterar o resultado.
        var divergente = await Fase2Apoio.RegistrarEntradaAsync(
            caixa,
            vagaId,
            chave,
            modelo: "Outro modelo");
        Assert.AreEqual(HttpStatusCode.Conflict, divergente.StatusCode);
        Assert.AreEqual("idempotency_conflict", await Fase2Apoio.CodigoDoErroAsync(divergente));
    }

    [TestMethod]
    public async Task ContratosDaEntradaRecusamCamposDoServidorEValidamChave()
    {
        using var fabrica = Fase2Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);
        var (manobrista, _) = await Fase2Apoio.EntrarManobristaAsync(fabrica);

        var vagaId = await Fase2Apoio.PrepararEntradaAsync(administrador);

        var semChave = await Fase2Apoio.RegistrarEntradaAsync(caixa, vagaId);
        Assert.AreEqual(HttpStatusCode.BadRequest, semChave.StatusCode);
        Assert.AreEqual("idempotency_key_invalida", await Fase2Apoio.CodigoDoErroAsync(semChave));

        var chaveInvalida = new HttpRequestMessage(HttpMethod.Post, "/api/v1/attendances")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    placa = "ABC1D23",
                    modelo = "Modelo",
                    titular = "Titular",
                    documento = "123",
                    vistoria = "Vistoria",
                    vagaId
                }),
                Encoding.UTF8,
                "application/json")
        };
        chaveInvalida.Headers.Add("Idempotency-Key", "nao-e-uuid");
        var respostaChaveInvalida = await caixa.SendAsync(chaveInvalida);
        Assert.AreEqual(HttpStatusCode.BadRequest, respostaChaveInvalida.StatusCode);

        var comTarifaDoCliente = await Fase2Apoio.RegistrarEntradaAsync(
            caixa,
            vagaId,
            Guid.NewGuid(),
            extras: new { tarifa = 10.00m });
        Assert.AreEqual(HttpStatusCode.BadRequest, comTarifaDoCliente.StatusCode);

        var comOperadorDoCliente = await Fase2Apoio.RegistrarEntradaAsync(
            caixa,
            vagaId,
            Guid.NewGuid(),
            extras: new { operadorId = Guid.NewGuid() });
        Assert.AreEqual(HttpStatusCode.BadRequest, comOperadorDoCliente.StatusCode);

        var incompleta = await Fase2Apoio.RegistrarEntradaAsync(
            caixa,
            vagaId,
            Guid.NewGuid(),
            documento: "   ");
        Assert.AreEqual(HttpStatusCode.BadRequest, incompleta.StatusCode);
        Assert.AreEqual("entrada_invalida", await Fase2Apoio.CodigoDoErroAsync(incompleta));

        var negada = await Fase2Apoio.RegistrarEntradaAsync(manobrista, vagaId, Guid.NewGuid());
        Assert.AreEqual(HttpStatusCode.Forbidden, negada.StatusCode);

        using (var escopo = fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
            Assert.AreEqual(0, await db.Atendimentos.CountAsync());
            Assert.AreEqual(0, await db.Ocupacoes.CountAsync());
        }
    }

    [TestMethod]
    public async Task EntradaSemTarifaVagaInexistenteOuOcupadaNaoCriaRegistrosOrfaos()
    {
        using var fabrica = Fase2Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        using var vaga = await Fase2Apoio.CriarVagaAsync(administrador, "A-01");
        var vagaId = vaga.RootElement.GetProperty("id").GetGuid();

        var semTarifa = await Fase2Apoio.RegistrarEntradaAsync(caixa, vagaId, Guid.NewGuid());
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, semTarifa.StatusCode);
        Assert.AreEqual("rate_not_published", await Fase2Apoio.CodigoDoErroAsync(semTarifa));

        await Fase2Apoio.PublicarTarifaAsync(administrador, 20.00m);

        var inexistente = await Fase2Apoio.RegistrarEntradaAsync(caixa, Guid.NewGuid(), Guid.NewGuid());
        Assert.AreEqual(HttpStatusCode.Conflict, inexistente.StatusCode);
        Assert.AreEqual("space_unavailable", await Fase2Apoio.CodigoDoErroAsync(inexistente));

        var primeira = await Fase2Apoio.RegistrarEntradaAsync(caixa, vagaId, Guid.NewGuid());
        Assert.AreEqual(HttpStatusCode.Created, primeira.StatusCode);

        var ocupada = await Fase2Apoio.RegistrarEntradaAsync(caixa, vagaId, Guid.NewGuid());
        Assert.AreEqual(HttpStatusCode.Conflict, ocupada.StatusCode);
        Assert.AreEqual("space_unavailable", await Fase2Apoio.CodigoDoErroAsync(ocupada));

        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
        Assert.AreEqual(1, await db.Atendimentos.CountAsync());
        Assert.AreEqual(1, await db.Ocupacoes.CountAsync());
        Assert.AreEqual(1, await db.ResultadosIdempotentes.CountAsync());
    }

    [TestMethod]
    public async Task ConcorrenciaNaMesmaVagaProduzUmUnico201()
    {
        using var fabrica = Fase2Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        var vagaId = await Fase2Apoio.PrepararEntradaAsync(administrador);

        var primeira = Fase2Apoio.RegistrarEntradaAsync(caixa, vagaId, Guid.NewGuid());
        var segunda = Fase2Apoio.RegistrarEntradaAsync(administrador, vagaId, Guid.NewGuid());

        var respostas = await Task.WhenAll(primeira, segunda);

        Assert.AreEqual(1, respostas.Count(resposta => resposta.StatusCode == HttpStatusCode.Created));
        Assert.AreEqual(1, respostas.Count(resposta => resposta.StatusCode == HttpStatusCode.Conflict));

        var conflito = respostas.Single(resposta => resposta.StatusCode == HttpStatusCode.Conflict);
        Assert.AreEqual("space_unavailable", await Fase2Apoio.CodigoDoErroAsync(conflito));

        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
        Assert.AreEqual(1, await db.Atendimentos.CountAsync());
        Assert.AreEqual(1, await db.Ocupacoes.CountAsync(ocupacao => ocupacao.FimUtc == null));
    }

    [TestMethod]
    public async Task IdentificadoresSaoUnicosSobEscritasConcorrentesEmVagasDiferentes()
    {
        using var fabrica = Fase2Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        using var vaga1 = await Fase2Apoio.CriarVagaAsync(administrador, "A-01");
        using var vaga2 = await Fase2Apoio.CriarVagaAsync(administrador, "A-02");
        await Fase2Apoio.PublicarTarifaAsync(administrador, 20.00m);

        var primeira = Fase2Apoio.RegistrarEntradaAsync(
            caixa,
            vaga1.RootElement.GetProperty("id").GetGuid(),
            Guid.NewGuid(),
            placa: "AAA1A11");
        var segunda = Fase2Apoio.RegistrarEntradaAsync(
            caixa,
            vaga2.RootElement.GetProperty("id").GetGuid(),
            Guid.NewGuid(),
            placa: "BBB2B22");

        var respostas = await Task.WhenAll(primeira, segunda);
        Assert.IsTrue(respostas.All(resposta => resposta.StatusCode == HttpStatusCode.Created));

        var identificadores = new List<string>();
        foreach (var resposta in respostas)
        {
            using var json = await Fase2Apoio.LerJsonAsync(resposta);
            identificadores.Add(json.RootElement.GetProperty("identificador").GetString()!);
        }

        Assert.AreEqual(2, identificadores.Distinct(StringComparer.Ordinal).Count());
        Assert.IsTrue(identificadores.All(IdentificadorAtendimento.EhValido));
    }

    private static bool Contem(byte[] origem, byte[] procurado)
    {
        if (procurado.Length == 0 || origem.Length < procurado.Length)
        {
            return false;
        }

        for (var i = 0; i <= origem.Length - procurado.Length; i++)
        {
            var igual = true;
            for (var j = 0; j < procurado.Length; j++)
            {
                if (origem[i + j] != procurado[j])
                {
                    igual = false;
                    break;
                }
            }

            if (igual)
            {
                return true;
            }
        }

        return false;
    }
}
