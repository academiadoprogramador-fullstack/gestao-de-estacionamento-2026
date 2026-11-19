using System.Net;
using System.Text.Json;
using Estacionamento.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Estacionamento.IntegrationTests;

[TestClass]
public sealed class RetiradaFilaTestes
{
    [TestMethod]
    public async Task FilaPaginaEProjetaPorPerfilSemVazarFinancasAoManobrista()
    {
        using var fabrica = Fase3Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);
        var (manobrista, _) = await Fase2Apoio.EntrarManobristaAsync(fabrica);

        await Fase2Apoio.PublicarTarifaAsync(administrador, 20.00m);
        var primeiro = await Fase3Apoio.PrepararAtendimentoComTarifaPublicadaAsync(
            administrador,
            caixa,
            "A-01",
            "ABC1D23");
        var segundo = await Fase3Apoio.PrepararAtendimentoComTarifaPublicadaAsync(
            administrador,
            caixa,
            "A-02",
            "DEF4G56");

        await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, primeiro);
        await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, segundo);

        using var filaAdministrador = await Fase3Apoio.FilaAsync(administrador, pagina: 1, tamanho: 1);
        Assert.AreEqual(2, filaAdministrador.RootElement.GetProperty("total").GetInt32());
        Assert.AreEqual(1, filaAdministrador.RootElement.GetProperty("items").GetArrayLength());
        var itemAdministrador = filaAdministrador.RootElement.GetProperty("items")[0];
        Assert.IsTrue(itemAdministrador.TryGetProperty("totalOriginal", out _));
        Assert.IsTrue(itemAdministrador.TryGetProperty("saldo", out _));
        Assert.IsTrue(itemAdministrador.TryGetProperty("autorizacaoDocumental", out _));
        Assert.IsTrue(itemAdministrador.TryGetProperty("versaoPedido", out _));

        using var filaManobrista = await Fase3Apoio.FilaAsync(manobrista);
        var itemManobrista = filaManobrista.RootElement.GetProperty("items")[0];
        Assert.IsTrue(itemManobrista.TryGetProperty("placa", out _));
        Assert.IsTrue(itemManobrista.TryGetProperty("versaoPedido", out _));
        Assert.IsFalse(itemManobrista.TryGetProperty("totalOriginal", out _));
        Assert.IsFalse(itemManobrista.TryGetProperty("saldo", out _));
        Assert.IsFalse(itemManobrista.TryGetProperty("conferencia", out _));
        Assert.IsFalse(itemManobrista.TryGetProperty("autorizacaoDocumental", out _));

        using var detalheCaixa = await Fase3Apoio.DetalheAsync(caixa, itemAdministrador.GetProperty("id").GetGuid());
        Assert.IsTrue(detalheCaixa.RootElement.TryGetProperty("conferencia", out _));
        Assert.IsTrue(detalheCaixa.RootElement.TryGetProperty("lancamentos", out _));
        Assert.IsTrue(detalheCaixa.RootElement.TryGetProperty("revisaoRazao", out _));

        using var detalheManobrista = await Fase3Apoio.DetalheAsync(
            manobrista,
            itemAdministrador.GetProperty("id").GetGuid());
        Assert.IsFalse(detalheManobrista.RootElement.TryGetProperty("conferencia", out _));
        Assert.IsFalse(detalheManobrista.RootElement.TryGetProperty("lancamentos", out _));
        Assert.IsFalse(detalheManobrista.RootElement.TryGetProperty("saldo", out _));

        var respostaFiltroInvalido = await administrador.GetAsync("/api/v1/withdrawals?state=entregue");
        Assert.AreEqual(HttpStatusCode.BadRequest, respostaFiltroInvalido.StatusCode);
        Assert.AreEqual("filtro_invalido", await Fase3Apoio.CodigoAsync(respostaFiltroInvalido));

        var respostaInexistente = await administrador.GetAsync($"/api/v1/withdrawals/{Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.NotFound, respostaInexistente.StatusCode);

        var caixaNegado = await manobrista.GetAsync(
            $"/api/v1/withdrawals/{itemAdministrador.GetProperty("id").GetGuid()}/cashier");
        Assert.AreEqual(HttpStatusCode.Forbidden, caixaNegado.StatusCode);

        using var historicoManobrista = JsonDocument.Parse(
            await (await manobrista.GetAsync($"/api/v1/attendances/{primeiro}/history?page=1&pageSize=50"))
                .Content.ReadAsStringAsync());
        var tipos = historicoManobrista.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("tipo").GetString())
            .ToArray();

        CollectionAssert.Contains(tipos, "pedido_retirada_registrado");
        CollectionAssert.DoesNotContain(tipos, "conferencia_retirada_registrada");
        CollectionAssert.DoesNotContain(tipos, "calculo_cobranca_fixado");
    }

    [TestMethod]
    public async Task ListaAtendimentosEmAbertoComSituacaoDaRetiradaSemDadosSensiveis()
    {
        using var fabrica = Fase3Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);
        var (manobrista, _) = await Fase2Apoio.EntrarManobristaAsync(fabrica);

        var comPedido = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa, tarifa: 20.00m);
        var semPedido = await Fase3Apoio.PrepararAtendimentoComTarifaPublicadaAsync(
            administrador,
            caixa,
            "A-02",
            "DEF4G56");
        var pedidoId = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, comPedido);

        var entregue = await Fase3Apoio.PrepararAtendimentoComTarifaPublicadaAsync(
            administrador,
            caixa,
            "A-03",
            "JKL7M89");
        SqlTeste.Executar(
            fabrica.NomeBanco,
            $"UPDATE [Atendimentos] SET [Estado] = 'entregue' WHERE [Id] = '{entregue}'; " +
            $"UPDATE [Ocupacoes] SET [FimUtc] = SYSDATETIMEOFFSET() WHERE [AtendimentoId] = '{entregue}';");

        var primeiraPagina = await manobrista.GetAsync(
            "/api/v1/attendances?status=aberto&page=1&pageSize=1");
        Assert.AreEqual(HttpStatusCode.OK, primeiraPagina.StatusCode);
        using var pagina = JsonDocument.Parse(await primeiraPagina.Content.ReadAsStringAsync());
        Assert.AreEqual(2, pagina.RootElement.GetProperty("total").GetInt32());
        Assert.AreEqual(1, pagina.RootElement.GetProperty("items").GetArrayLength());
        var item = pagina.RootElement.GetProperty("items")[0];
        Assert.IsTrue(item.TryGetProperty("identificador", out _));
        Assert.IsTrue(item.TryGetProperty("vagaCodigo", out _));
        Assert.IsTrue(item.TryGetProperty("estadoRetirada", out _));
        Assert.IsFalse(item.TryGetProperty("saldo", out _));
        Assert.IsFalse(item.TryGetProperty("totalOriginal", out _));

        var segundaPagina = await caixa.GetAsync(
            "/api/v1/attendances?status=aberto&page=2&pageSize=1");
        Assert.AreEqual(HttpStatusCode.OK, segundaPagina.StatusCode);
        using var pagina2 = JsonDocument.Parse(await segundaPagina.Content.ReadAsStringAsync());
        Assert.AreEqual(1, pagina2.RootElement.GetProperty("items").GetArrayLength());

        var comPedidoNaLista = await caixa.GetAsync(
            "/api/v1/attendances?status=aberto&page=1&pageSize=20");
        using var todos = JsonDocument.Parse(await comPedidoNaLista.Content.ReadAsStringAsync());
        var itemComPedido = todos.RootElement.GetProperty("items")
            .EnumerateArray()
            .Single(registro => registro.GetProperty("id").GetGuid() == comPedido);
        Assert.AreEqual(pedidoId, itemComPedido.GetProperty("pedidoId").GetGuid());
        Assert.AreEqual("aguardando", itemComPedido.GetProperty("estadoRetirada").GetString());

        var itemSemPedido = todos.RootElement.GetProperty("items")
            .EnumerateArray()
            .Single(registro => registro.GetProperty("id").GetGuid() == semPedido);
        Assert.AreEqual(JsonValueKind.Null, itemSemPedido.GetProperty("pedidoId").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, itemSemPedido.GetProperty("estadoRetirada").ValueKind);

        Assert.IsFalse(todos.RootElement.GetProperty("items")
            .EnumerateArray()
            .Any(registro => registro.GetProperty("id").GetGuid() == entregue));

        var combinado = await administrador.GetAsync(
            "/api/v1/attendances?status=aberto&plate=ABC1D23");
        Assert.AreEqual(HttpStatusCode.BadRequest, combinado.StatusCode);
        Assert.AreEqual("filtro_invalido", await Fase3Apoio.CodigoAsync(combinado));

        var statusInvalido = await administrador.GetAsync("/api/v1/attendances?status=encerrado");
        Assert.AreEqual(HttpStatusCode.BadRequest, statusInvalido.StatusCode);

        var buscaExata = await administrador.GetAsync("/api/v1/attendances?plate=DEF4G56");
        Assert.AreEqual(HttpStatusCode.OK, buscaExata.StatusCode);
    }

    [TestMethod]
    public async Task BuscaRegistraResponsavelHorarioEImpedeTransicaoConcorrente()
    {
        using var fabrica = Fase3Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);
        var (manobrista, _) = await Fase2Apoio.EntrarManobristaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa);
        var pedidoId = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, atendimentoId);

        using var detalhe = await Fase3Apoio.DetalheAsync(manobrista, pedidoId);
        var versaoInicial = Fase3Apoio.VersaoDoPedido(detalhe);

        using var busca = await Fase3Apoio.IniciarBuscaAsync(manobrista, pedidoId, versaoInicial);
        Assert.AreEqual(HttpStatusCode.OK, busca.StatusCode, await busca.Content.ReadAsStringAsync());
        using var buscaJson = await Fase2Apoio.LerJsonAsync(busca);
        Assert.AreEqual("em busca", buscaJson.RootElement.GetProperty("estado").GetString());
        Assert.IsFalse(string.IsNullOrWhiteSpace(
            buscaJson.RootElement.GetProperty("responsavelBusca").GetString()));
        Assert.AreNotEqual(
            JsonValueKind.Null,
            buscaJson.RootElement.GetProperty("buscaIniciadaEmUtc").ValueKind);

        using var repetida = await Fase3Apoio.IniciarBuscaAsync(manobrista, pedidoId, versaoInicial);
        Assert.AreEqual(HttpStatusCode.Conflict, repetida.StatusCode);
        Assert.AreEqual("stale_withdrawal_version", await Fase3Apoio.CodigoAsync(repetida));

        using var detalheAtual = await Fase3Apoio.DetalheAsync(manobrista, pedidoId);
        var versaoAtual = Fase3Apoio.VersaoDoPedido(detalheAtual);
        using var emBusca = await Fase3Apoio.IniciarBuscaAsync(manobrista, pedidoId, versaoAtual);
        Assert.AreEqual(HttpStatusCode.Conflict, emBusca.StatusCode);
        Assert.AreEqual("withdrawal_not_waiting", await Fase3Apoio.CodigoAsync(emBusca));

        var segundoAtendimento = await Fase3Apoio.PrepararAtendimentoComTarifaPublicadaAsync(
            administrador,
            caixa,
            "B-01",
            "JKL7M89");
        var segundoPedido = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, segundoAtendimento);
        using var detalheSegundo = await Fase3Apoio.DetalheAsync(caixa, segundoPedido);
        var versaoSegundo = Fase3Apoio.VersaoDoPedido(detalheSegundo);

        var concorrentes = await Task.WhenAll(
            Fase3Apoio.IniciarBuscaAsync(caixa, segundoPedido, versaoSegundo),
            Fase3Apoio.IniciarBuscaAsync(manobrista, segundoPedido, versaoSegundo));

        Assert.AreEqual(
            1,
            concorrentes.Count(resposta => resposta.StatusCode == HttpStatusCode.OK));
        Assert.AreEqual(
            1,
            concorrentes.Count(resposta => resposta.StatusCode == HttpStatusCode.Conflict));

        foreach (var resposta in concorrentes)
        {
            resposta.Dispose();
        }

        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
        var atendimento = await db.Atendimentos.AsNoTracking()
            .SingleAsync(registro => registro.Id == atendimentoId);
        Assert.AreEqual("em busca", atendimento.Estado);
        Assert.AreEqual(1, await db.Ocupacoes.CountAsync(ocupacao =>
            ocupacao.AtendimentoId == atendimentoId && ocupacao.FimUtc == null));

        var entregaGet = await manobrista.GetAsync($"/api/v1/withdrawals/{pedidoId}/delivery");
        Assert.AreEqual(HttpStatusCode.NotFound, entregaGet.StatusCode);

        var entregaPost = await manobrista.PostAsync(
            $"/api/v1/withdrawals/{pedidoId}/delivery",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        Assert.IsTrue(
            entregaPost.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
            $"Não deve existir rota de entrega nesta fase (recebido {(int)entregaPost.StatusCode}).");

        var consumo = await administrador.PostAsync(
            $"/api/v1/withdrawals/{pedidoId}/authorizations/{Guid.NewGuid()}/consume",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        Assert.IsTrue(
            consumo.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
    }
}
