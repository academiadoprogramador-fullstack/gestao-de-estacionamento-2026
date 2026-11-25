using System.Net;
using System.Text.Json;
using Estacionamento.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Estacionamento.IntegrationTests;

[TestClass]
public sealed class EntregaFase4Testes
{
    [TestMethod]
    public async Task VistoriaSaidaEhUnicaImutavelELeituraEhAuditadaNosTresPerfis()
    {
        using var fabrica = Fase4Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);
        var (manobrista, _) = await Fase2Apoio.EntrarManobristaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa);
        var pedidoId = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, atendimentoId);

        var versao = await Fase4Apoio.VersaoAtendimentoAsync(manobrista, atendimentoId);
        var chave = Guid.NewGuid();
        using var criada = await Fase4Apoio.RegistrarVistoriaAsync(
            manobrista,
            atendimentoId,
            "Riscos leves na lateral direita.",
            versao,
            chave);

        Assert.AreEqual(HttpStatusCode.Created, criada.StatusCode, await criada.Content.ReadAsStringAsync());
        using var recibo = JsonDocument.Parse(await criada.Content.ReadAsStringAsync());
        var vistoriaId = recibo.RootElement.GetProperty("vistoriaId").GetGuid();
        Assert.IsFalse(recibo.RootElement.TryGetProperty("texto", out _));
        Assert.IsFalse((await criada.Content.ReadAsStringAsync()).Contains("Riscos leves", StringComparison.Ordinal));

        foreach (var perfil in new[] { administrador, caixa, manobrista })
        {
            using var leitura = await Fase4Apoio.LerVistoriaAsync(perfil, atendimentoId);
            Assert.AreEqual(HttpStatusCode.OK, leitura.StatusCode, await leitura.Content.ReadAsStringAsync());
            using var conteudo = JsonDocument.Parse(await leitura.Content.ReadAsStringAsync());
            Assert.AreEqual(
                "Riscos leves na lateral direita.",
                conteudo.RootElement.GetProperty("conteudo").GetString());
            Assert.IsFalse(string.IsNullOrWhiteSpace(conteudo.RootElement.GetProperty("ator").GetString()));
            Assert.AreEqual(
                "no-store",
                leitura.Headers.CacheControl?.ToString().Split(',').First().Trim());
        }

        using (var escopo = fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
            Assert.AreEqual(3, await db.EventosAuditoriaNegocio.CountAsync(
                evento => evento.Tipo == "vistoria_saida_lida"));
            Assert.AreEqual(1, await db.VistoriasSaida.CountAsync());
        }

        using var repeticao = await Fase4Apoio.RegistrarVistoriaAsync(
            manobrista,
            atendimentoId,
            "Riscos leves na lateral direita.",
            versao,
            chave);
        Assert.AreEqual(HttpStatusCode.Created, repeticao.StatusCode);
        using var reciboRepetido = JsonDocument.Parse(await repeticao.Content.ReadAsStringAsync());
        Assert.AreEqual(vistoriaId, reciboRepetido.RootElement.GetProperty("vistoriaId").GetGuid());

        using var conflitoConteudo = await Fase4Apoio.RegistrarVistoriaAsync(
            manobrista,
            atendimentoId,
            "Outro texto.",
            versao,
            chave);
        Assert.AreEqual(HttpStatusCode.Conflict, conflitoConteudo.StatusCode);
        Assert.AreEqual("idempotency_conflict", await Fase4Apoio.CodigoAsync(conflitoConteudo));

        using var duplicada = await Fase4Apoio.RegistrarVistoriaAsync(
            manobrista,
            atendimentoId,
            "Tentativa de substituição.",
            versao);
        Assert.AreEqual(HttpStatusCode.Conflict, duplicada.StatusCode);
        Assert.AreEqual("exit_inspection_already_exists", await Fase4Apoio.CodigoAsync(duplicada));

        using var vazio = await Fase4Apoio.RegistrarVistoriaAsync(manobrista, atendimentoId, "   ", versao);
        Assert.AreEqual(HttpStatusCode.BadRequest, vazio.StatusCode);

        using var excessivo = await Fase4Apoio.RegistrarVistoriaAsync(
            manobrista,
            atendimentoId,
            new string('x', 4001),
            versao);
        Assert.AreEqual(HttpStatusCode.BadRequest, excessivo.StatusCode);

        using var propriedadeInesperada = await Fase4Apoio.RegistrarVistoriaAsync(
            manobrista,
            atendimentoId,
            "Texto válido.",
            versao,
            extras: new { ator = Guid.NewGuid(), horario = DateTimeOffset.UtcNow });
        Assert.AreEqual(HttpStatusCode.BadRequest, propriedadeInesperada.StatusCode);

        using (var escopo = fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
            var vistoria = await db.VistoriasSaida.AsNoTracking().SingleAsync();
            Assert.AreEqual(vistoriaId, vistoria.Id);
            Assert.AreEqual(pedidoId, vistoria.PedidoId);
            Assert.IsFalse(vistoria.TextoCifrado.Length == 0);
        }

        using var detalhe = await administrador.GetAsync($"/api/v1/withdrawals/{pedidoId}");
        var corpoDetalhe = await detalhe.Content.ReadAsStringAsync();
        Assert.IsFalse(corpoDetalhe.Contains("Riscos leves", StringComparison.Ordinal));
        using var jsonDetalhe = JsonDocument.Parse(corpoDetalhe);
        Assert.IsTrue(jsonDetalhe.RootElement.GetProperty("vistoriaSaidaRegistrada").GetBoolean());

        using var atendimentoSemPedido = await Fase2Apoio.RegistrarEntradaAsync(
            caixa,
            (await Fase2Apoio.CriarVagaAsync(administrador, "Z-99")).RootElement.GetProperty("id").GetGuid(),
            chave: Guid.NewGuid(),
            placa: "ZZZ9Z99");
        using var entrada = JsonDocument.Parse(await atendimentoSemPedido.Content.ReadAsStringAsync());
        var semPedidoId = entrada.RootElement.GetProperty("atendimentoId").GetGuid();
        var versaoSemPedido = await Fase4Apoio.VersaoAtendimentoAsync(manobrista, semPedidoId);
        using var semPedido = await Fase4Apoio.RegistrarVistoriaAsync(
            manobrista,
            semPedidoId,
            "Texto.",
            versaoSemPedido);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, semPedido.StatusCode);
        Assert.AreEqual("exit_inspection_requires_withdrawal", await Fase4Apoio.CodigoAsync(semPedido));

        // Envelope corrompido (chave/formato inválido) falha fechado: a API
        // não devolve vazio, texto puro nem parcial.
        SqlTeste.Executar(
            fabrica.NomeBanco,
            $"UPDATE [VistoriasSaida] SET [TextoCifrado] = 0x00 WHERE [AtendimentoId] = '{atendimentoId}';");
        using var leituraCorrompida = await Fase4Apoio.LerVistoriaAsync(administrador, atendimentoId);
        Assert.AreEqual(HttpStatusCode.InternalServerError, leituraCorrompida.StatusCode);
        var corpoCorrompido = await leituraCorrompida.Content.ReadAsStringAsync();
        Assert.IsFalse(corpoCorrompido.Contains("Riscos leves", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task OcupacaoAusenteBloqueiaEntregaSemConsumirOuLiberar()
    {
        using var fabrica = Fase4Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa);
        var pedidoId = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, atendimentoId);
        await Fase4Apoio.RegistrarVistoriaConfirmadaAsync(caixa, atendimentoId);
        using (var caixaInicial = await Fase3Apoio.CaixaAsync(caixa, pedidoId))
        {
            using var pagamento = await Fase3Apoio.PagarAsync(
                caixa,
                pedidoId,
                Fase3Apoio.Saldo(caixaInicial),
                "pix",
                Fase3Apoio.RevisaoDaRazao(caixaInicial));
            Assert.AreEqual(HttpStatusCode.Created, pagamento.StatusCode);
        }

        SqlTeste.Executar(
            fabrica.NomeBanco,
            $"DELETE FROM [Ocupacoes] WHERE [AtendimentoId] = '{atendimentoId}';");

        using var prontidao = await Fase4Apoio.ProntidaoAsync(caixa, pedidoId);
        Assert.IsFalse(prontidao.RootElement.GetProperty("pronta").GetBoolean());
        CollectionAssert.Contains(
            prontidao.RootElement.GetProperty("bloqueios")
                .EnumerateArray()
                .Select(item => item.GetString() ?? string.Empty)
                .ToArray(),
            "delivery_requires_open_occupancy");

        using var entrega = await Fase4Apoio.ConfirmarEntregaAsync(caixa, pedidoId, prontidao);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, entrega.StatusCode);
        Assert.AreEqual("delivery_requires_open_occupancy", await Fase4Apoio.CodigoAsync(entrega));

        using (var escopo = fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
            Assert.AreEqual(0, await db.EntregasVeiculo.CountAsync());
            Assert.AreEqual(0, await db.ConsumosAutorizacao.CountAsync());
            Assert.AreEqual("aguardando", (await db.Atendimentos.AsNoTracking().SingleAsync()).Estado);
        }
    }

    [TestMethod]
    public async Task EntregaNormalLiberaVagaUmaVezEReutilizaSemDuplicarEfeitos()
    {
        using var fabrica = Fase4Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);
        var (manobrista, _) = await Fase2Apoio.EntrarManobristaAsync(fabrica);

        using var vaga = await Fase2Apoio.CriarVagaAsync(administrador, "B-01");
        var vagaId = vaga.RootElement.GetProperty("id").GetGuid();
        await Fase2Apoio.PublicarTarifaAsync(administrador, 20.00m);
        var atendimentoId = await Fase3Apoio.RegistrarEntradaAsync(caixa, vagaId, "ABC1D23");
        var pedidoId = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, atendimentoId);
        await Fase4Apoio.RegistrarVistoriaConfirmadaAsync(manobrista, atendimentoId);

        using (var caixaInicial = await Fase3Apoio.CaixaAsync(caixa, pedidoId))
        {
            using var pagamento = await Fase3Apoio.PagarAsync(
                caixa,
                pedidoId,
                Fase3Apoio.Saldo(caixaInicial),
                "dinheiro",
                Fase3Apoio.RevisaoDaRazao(caixaInicial));
            Assert.AreEqual(HttpStatusCode.Created, pagamento.StatusCode, await pagamento.Content.ReadAsStringAsync());
        }

        using var prontidao = await Fase4Apoio.ProntidaoAsync(caixa, pedidoId);
        Assert.IsTrue(prontidao.RootElement.GetProperty("pronta").GetBoolean());
        Assert.AreEqual(0, prontidao.RootElement.GetProperty("bloqueios").GetArrayLength());
        Assert.IsTrue(prontidao.RootElement.GetProperty("vistoriaRegistrada").GetBoolean());

        var chave = Guid.NewGuid();
        using var entrega = await Fase4Apoio.ConfirmarEntregaAsync(caixa, pedidoId, prontidao, chave);
        Assert.AreEqual(HttpStatusCode.Created, entrega.StatusCode, await entrega.Content.ReadAsStringAsync());
        using var recibo = JsonDocument.Parse(await entrega.Content.ReadAsStringAsync());
        var entregaId = recibo.RootElement.GetProperty("entregaId").GetGuid();
        Assert.AreEqual("entregue", recibo.RootElement.GetProperty("estado").GetString());
        Assert.AreEqual("B-01", recibo.RootElement.GetProperty("liberacao").GetProperty("vagaCodigo").GetString());
        Assert.IsFalse(recibo.RootElement.TryGetProperty("saldo", out _));

        using (var escopo = fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
            var entregaBanco = await db.EntregasVeiculo.AsNoTracking().SingleAsync();
            Assert.AreEqual(entregaId, entregaBanco.Id);
            var ocupacao = await db.Ocupacoes.AsNoTracking().SingleAsync(registro => registro.Id == entregaBanco.OcupacaoId);
            Assert.AreEqual(entregaBanco.EntregueEmUtc, ocupacao.FimUtc);
            Assert.AreEqual("entregue", (await db.Atendimentos.AsNoTracking().SingleAsync()).Estado);
            Assert.AreEqual("entregue", (await db.PedidosRetirada.AsNoTracking().SingleAsync()).Estado);
            Assert.AreEqual(1, await db.Ocupacoes.CountAsync());
        }

        using var filaEntregue = await Fase3Apoio.FilaAsync(manobrista, "entregue");
        Assert.AreEqual(1, filaEntregue.RootElement.GetProperty("total").GetInt32());
        var item = filaEntregue.RootElement.GetProperty("items")[0];
        Assert.AreEqual("entregue", item.GetProperty("estado").GetString());
        Assert.AreEqual(entregaId, item.GetProperty("entrega").GetProperty("entregaId").GetGuid());
        Assert.AreEqual("B-01", item.GetProperty("entrega").GetProperty("vagaCodigo").GetString());
        Assert.IsFalse(item.TryGetProperty("saldo", out _));

        using var detalheAtendimento = JsonDocument.Parse(
            await (await manobrista.GetAsync($"/api/v1/attendances/{atendimentoId}"))
                .Content.ReadAsStringAsync());
        Assert.AreEqual("entregue", detalheAtendimento.RootElement.GetProperty("estado").GetString());
        Assert.IsTrue(detalheAtendimento.RootElement.GetProperty("vistoriaSaidaRegistrada").GetBoolean());
        Assert.AreEqual(
            entregaId,
            detalheAtendimento.RootElement.GetProperty("entrega").GetProperty("entregaId").GetGuid());
        Assert.IsFalse(detalheAtendimento.RootElement.TryGetProperty("tarifaValorSnapshot", out _));

        using var caixaDetalhe = await Fase3Apoio.CaixaAsync(caixa, pedidoId);
        Assert.AreEqual(entregaId, caixaDetalhe.RootElement.GetProperty("entrega").GetProperty("entregaId").GetGuid());

        using var repeticao = await Fase4Apoio.ConfirmarEntregaAsync(caixa, pedidoId, prontidao, chave);
        Assert.AreEqual(HttpStatusCode.Created, repeticao.StatusCode, await repeticao.Content.ReadAsStringAsync());
        using var reciboRepetido = JsonDocument.Parse(await repeticao.Content.ReadAsStringAsync());
        Assert.AreEqual(entregaId, reciboRepetido.RootElement.GetProperty("entregaId").GetGuid());

        using var novaChave = await Fase4Apoio.ConfirmarEntregaAsync(
            caixa,
            pedidoId,
            await Fase4Apoio.ProntidaoAsync(caixa, pedidoId),
            Guid.NewGuid());
        Assert.AreEqual(HttpStatusCode.Conflict, novaChave.StatusCode);
        Assert.AreEqual("delivery_already_completed", await Fase4Apoio.CodigoAsync(novaChave));

        using var chaveDivergente = await Fase4Apoio.ConfirmarEntregaAsync(
            caixa,
            pedidoId,
            prontidao,
            chave,
            sobrescritas: new { versaoRazao = Convert.ToBase64String([9, 9, 9, 9, 9, 9, 9, 9]) });
        Assert.AreEqual(HttpStatusCode.Conflict, chaveDivergente.StatusCode);
        Assert.AreEqual("idempotency_conflict", await Fase4Apoio.CodigoAsync(chaveDivergente));

        using var novaConcessao = await Fase3Apoio.ConcederAutorizacaoAsync(
            administrador,
            pedidoId,
            "documental",
            "Tentativa após entrega.",
            Convert.ToBase64String([1, 2, 3, 4, 5, 6, 7, 8]));
        Assert.AreEqual(HttpStatusCode.Conflict, novaConcessao.StatusCode);
        Assert.AreEqual("delivery_already_completed", await Fase4Apoio.CodigoAsync(novaConcessao));

        using var novaVistoria = await Fase4Apoio.RegistrarVistoriaAsync(
            manobrista,
            atendimentoId,
            "Nova vistoria após entrega.",
            await Fase4Apoio.VersaoAtendimentoAsync(manobrista, atendimentoId));
        Assert.AreEqual(HttpStatusCode.Conflict, novaVistoria.StatusCode);
        Assert.AreEqual("delivery_already_completed", await Fase4Apoio.CodigoAsync(novaVistoria));

        using var novaEntrada = await Fase2Apoio.RegistrarEntradaAsync(
            caixa,
            vagaId,
            chave: Guid.NewGuid(),
            placa: "XYZ9Z99");
        Assert.AreEqual(HttpStatusCode.Created, novaEntrada.StatusCode, await novaEntrada.Content.ReadAsStringAsync());

        using (var escopo = fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
            Assert.AreEqual(1, await db.EntregasVeiculo.CountAsync());
            Assert.AreEqual(2, await db.Ocupacoes.CountAsync());
            Assert.AreEqual(1, await db.Ocupacoes.CountAsync(ocupacao => ocupacao.FimUtc == null));
        }
    }

    [TestMethod]
    public async Task EntregaEmBuscaComSaldoExigeAutorizacaoFinanceiraEConsomeUmaVezComAcertoPosterior()
    {
        using var fabrica = Fase4Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);
        var (manobrista, _) = await Fase2Apoio.EntrarManobristaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa);
        var pedidoId = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, atendimentoId);
        using var detalhe = await Fase3Apoio.DetalheAsync(caixa, pedidoId);
        using var busca = await Fase3Apoio.IniciarBuscaAsync(caixa, pedidoId, Fase3Apoio.VersaoDoPedido(detalhe));
        Assert.AreEqual(HttpStatusCode.OK, busca.StatusCode, await busca.Content.ReadAsStringAsync());

        await Fase4Apoio.RegistrarVistoriaConfirmadaAsync(manobrista, atendimentoId);

        using var bloqueada = await Fase4Apoio.ProntidaoAsync(caixa, pedidoId);
        Assert.IsFalse(bloqueada.RootElement.GetProperty("pronta").GetBoolean());
        CollectionAssert.Contains(
            bloqueada.RootElement.GetProperty("bloqueios")
                .EnumerateArray()
                .Select(item => item.GetString() ?? string.Empty)
                .ToArray(),
            "delivery_balance_outstanding");

        using var semAutorizacao = await Fase4Apoio.ConfirmarEntregaAsync(
            caixa,
            pedidoId,
            bloqueada);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, semAutorizacao.StatusCode);
        Assert.AreEqual("delivery_balance_outstanding", await Fase4Apoio.CodigoAsync(semAutorizacao));

        using var pedidoAtual = await Fase3Apoio.DetalheAsync(caixa, pedidoId);
        using var concessao = await Fase3Apoio.ConcederAutorizacaoAsync(
            administrador,
            pedidoId,
            "financeira",
            "Saldo a acertar após a entrega.",
            Fase3Apoio.VersaoDoPedido(pedidoAtual));
        Assert.AreEqual(HttpStatusCode.Created, concessao.StatusCode, await concessao.Content.ReadAsStringAsync());

        using var pronta = await Fase4Apoio.ProntidaoAsync(caixa, pedidoId);
        Assert.IsTrue(pronta.RootElement.GetProperty("pronta").GetBoolean());
        Assert.AreEqual(20.00m, pronta.RootElement.GetProperty("saldo").GetDecimal());

        using var entrega = await Fase4Apoio.ConfirmarEntregaAsync(caixa, pedidoId, pronta);
        Assert.AreEqual(HttpStatusCode.Created, entrega.StatusCode, await entrega.Content.ReadAsStringAsync());

        using var caixaPosEntrega = await Fase3Apoio.CaixaAsync(caixa, pedidoId);
        Assert.AreEqual("consumida", caixaPosEntrega.RootElement.GetProperty("autorizacaoFinanceira")
            .GetProperty("vigencia").GetString());
        Assert.IsTrue(caixaPosEntrega.RootElement.GetProperty("autorizacaoFinanceira")
            .GetProperty("consumidaEmUtc").ValueKind is JsonValueKind.String);
        Assert.AreEqual(20.00m, Fase3Apoio.Saldo(caixaPosEntrega));

        using (var escopo = fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
            Assert.AreEqual(1, await db.ConsumosAutorizacao.CountAsync());
            var consumo = await db.ConsumosAutorizacao.AsNoTracking().SingleAsync();
            Assert.AreEqual("financeira", consumo.Tipo);
            Assert.AreEqual(1, consumo.RevisaoReferencia);
        }

        using var pagamento = await Fase3Apoio.PagarAsync(
            caixa,
            pedidoId,
            20.00m,
            "pix",
            Fase3Apoio.RevisaoDaRazao(caixaPosEntrega));
        Assert.AreEqual(HttpStatusCode.Created, pagamento.StatusCode, await pagamento.Content.ReadAsStringAsync());

        using var caixaQuitado = await Fase3Apoio.CaixaAsync(caixa, pedidoId);
        Assert.AreEqual(0m, Fase3Apoio.Saldo(caixaQuitado));
        Assert.AreEqual("entregue", caixaQuitado.RootElement.GetProperty("estado").GetString());
        Assert.AreEqual("consumida", caixaQuitado.RootElement.GetProperty("autorizacaoFinanceira")
            .GetProperty("vigencia").GetString());

        using (var escopo = fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
            var entregaBanco = await db.EntregasVeiculo.AsNoTracking().SingleAsync();
            Assert.IsNotNull(entregaBanco);
            Assert.AreEqual(1, await db.ConsumosAutorizacao.CountAsync());
            Assert.AreEqual("entregue", (await db.Atendimentos.AsNoTracking().SingleAsync()).Estado);
            Assert.AreEqual(1, await db.LancamentosFinanceiros.CountAsync());
        }
    }

    [TestMethod]
    public async Task DivergenciaESaldoExigemAutorizacoesIndependentesEConsumosSeparados()
    {
        using var fabrica = Fase4Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa);
        var pedidoId = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(
            caixa,
            atendimentoId,
            conferencia: "divergente");
        await Fase4Apoio.RegistrarVistoriaConfirmadaAsync(funcionario: caixa, atendimentoId);

        using var bloqueada = await Fase4Apoio.ProntidaoAsync(caixa, pedidoId);
        var bloqueios = bloqueada.RootElement.GetProperty("bloqueios")
            .EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .ToArray();
        CollectionAssert.Contains(bloqueios, "delivery_document_not_approved");
        CollectionAssert.Contains(bloqueios, "delivery_balance_outstanding");

        using var detalhe = await Fase3Apoio.DetalheAsync(caixa, pedidoId);
        using var documental = await Fase3Apoio.ConcederAutorizacaoAsync(
            administrador,
            pedidoId,
            "documental",
            "Documento divergente justificado.",
            Fase3Apoio.VersaoDoPedido(detalhe));
        Assert.AreEqual(HttpStatusCode.Created, documental.StatusCode, await documental.Content.ReadAsStringAsync());

        using var apenasDocumental = await Fase4Apoio.ProntidaoAsync(caixa, pedidoId);
        Assert.IsFalse(apenasDocumental.RootElement.GetProperty("pronta").GetBoolean());
        CollectionAssert.DoesNotContain(
            apenasDocumental.RootElement.GetProperty("bloqueios")
                .EnumerateArray()
                .Select(item => item.GetString() ?? string.Empty)
                .ToArray(),
            "delivery_document_not_approved");

        using var detalheAtual = await Fase3Apoio.DetalheAsync(caixa, pedidoId);
        using var financeira = await Fase3Apoio.ConcederAutorizacaoAsync(
            administrador,
            pedidoId,
            "financeira",
            "Saldo excepcional justificado.",
            Fase3Apoio.VersaoDoPedido(detalheAtual));
        Assert.AreEqual(HttpStatusCode.Created, financeira.StatusCode, await financeira.Content.ReadAsStringAsync());

        using var pronta = await Fase4Apoio.ProntidaoAsync(caixa, pedidoId);
        Assert.IsTrue(pronta.RootElement.GetProperty("pronta").GetBoolean());

        using var entrega = await Fase4Apoio.ConfirmarEntregaAsync(caixa, pedidoId, pronta);
        Assert.AreEqual(HttpStatusCode.Created, entrega.StatusCode, await entrega.Content.ReadAsStringAsync());

        using var caixaPos = await Fase3Apoio.CaixaAsync(caixa, pedidoId);
        Assert.AreEqual("consumida", caixaPos.RootElement.GetProperty("autorizacaoDocumental")
            .GetProperty("vigencia").GetString());
        Assert.AreEqual("consumida", caixaPos.RootElement.GetProperty("autorizacaoFinanceira")
            .GetProperty("vigencia").GetString());

        using (var escopo = fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
            Assert.AreEqual(2, await db.ConsumosAutorizacao.CountAsync());
            Assert.AreEqual(0, await db.ConsumosAutorizacao.CountAsync(consumo => consumo.Tipo == "outro"));
        }

        using var historicoCaixa = JsonDocument.Parse(
            await (await caixa.GetAsync($"/api/v1/attendances/{atendimentoId}/history?page=1&pageSize=50"))
                .Content.ReadAsStringAsync());
        var tiposCaixa = historicoCaixa.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("tipo").GetString() ?? string.Empty)
            .ToArray();
        Assert.AreEqual(2, tiposCaixa.Count(tipo => tipo == "autorizacao_consumida"));
        CollectionAssert.Contains(tiposCaixa, "entrega_confirmada");
        CollectionAssert.Contains(tiposCaixa, "vaga_liberada");
    }

    [TestMethod]
    public async Task AutorizacaoRevogadaOuObsoletaAposDescontoBloqueiaSemEfeitosParciais()
    {
        using var fabrica = Fase4Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa, tarifa: 40.00m);
        var pedidoId = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, atendimentoId);
        await Fase4Apoio.RegistrarVistoriaConfirmadaAsync(caixa, atendimentoId);

        using var detalhe = await Fase3Apoio.DetalheAsync(caixa, pedidoId);
        using var concessao = await Fase3Apoio.ConcederAutorizacaoAsync(
            administrador,
            pedidoId,
            "financeira",
            "Saldo concedido antes do desconto.",
            Fase3Apoio.VersaoDoPedido(detalhe));
        Assert.AreEqual(HttpStatusCode.Created, concessao.StatusCode);

        using var caixaInicial = await Fase3Apoio.CaixaAsync(caixa, pedidoId);
        using var desconto = await Fase3Apoio.DescontarAsync(
            administrador,
            pedidoId,
            5.00m,
            "Desconto posterior que altera a revisão.",
            Fase3Apoio.RevisaoDaRazao(caixaInicial));
        Assert.AreEqual(HttpStatusCode.Created, desconto.StatusCode, await desconto.Content.ReadAsStringAsync());

        using var obsoleta = await Fase4Apoio.ProntidaoAsync(caixa, pedidoId);
        Assert.IsFalse(obsoleta.RootElement.GetProperty("pronta").GetBoolean());
        CollectionAssert.Contains(
            obsoleta.RootElement.GetProperty("bloqueios")
                .EnumerateArray()
                .Select(item => item.GetString() ?? string.Empty)
                .ToArray(),
            "delivery_authorization_stale");

        using var tentativa = await Fase4Apoio.ConfirmarEntregaAsync(caixa, pedidoId, obsoleta);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, tentativa.StatusCode);

        using (var escopo = fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
            Assert.AreEqual(0, await db.EntregasVeiculo.CountAsync());
            Assert.AreEqual(0, await db.ConsumosAutorizacao.CountAsync());
            Assert.AreEqual(
                "aguardando",
                (await db.Atendimentos.AsNoTracking().SingleAsync()).Estado);
            Assert.AreEqual(1, await db.Ocupacoes.CountAsync(ocupacao => ocupacao.FimUtc == null));
        }

        using var detalheAtual = await Fase3Apoio.DetalheAsync(caixa, pedidoId);
        using var novaConcessao = await Fase3Apoio.ConcederAutorizacaoAsync(
            administrador,
            pedidoId,
            "financeira",
            "Nova concessão vinculada à revisão atual.",
            Fase3Apoio.VersaoDoPedido(detalheAtual));
        Assert.AreEqual(HttpStatusCode.Created, novaConcessao.StatusCode);
        using var caixaNova = await Fase3Apoio.CaixaAsync(caixa, pedidoId);
        var autorizacaoId = caixaNova.RootElement.GetProperty("autorizacaoFinanceira").GetProperty("id").GetGuid();
        using var revogacao = await Fase3Apoio.RevogarAutorizacaoAsync(
            administrador,
            pedidoId,
            autorizacaoId,
            "Revogação antes da entrega.");
        Assert.AreEqual(HttpStatusCode.Created, revogacao.StatusCode, await revogacao.Content.ReadAsStringAsync());

        using var revogada = await Fase4Apoio.ProntidaoAsync(caixa, pedidoId);
        Assert.IsFalse(revogada.RootElement.GetProperty("pronta").GetBoolean());
        using var tentativaRevogada = await Fase4Apoio.ConfirmarEntregaAsync(caixa, pedidoId, revogada);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, tentativaRevogada.StatusCode);
        Assert.AreEqual("delivery_authorization_stale", await Fase4Apoio.CodigoAsync(tentativaRevogada));
    }

    [TestMethod]
    public async Task DuasEntregasConcorrentesProduzemUmaUnicaEntregaELiberacao()
    {
        using var fabrica = Fase4Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa);
        var pedidoId = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, atendimentoId);
        await Fase4Apoio.RegistrarVistoriaConfirmadaAsync(caixa, atendimentoId);

        using (var caixaInicial = await Fase3Apoio.CaixaAsync(caixa, pedidoId))
        {
            using var pagamento = await Fase3Apoio.PagarAsync(
                caixa,
                pedidoId,
                Fase3Apoio.Saldo(caixaInicial),
                "pix",
                Fase3Apoio.RevisaoDaRazao(caixaInicial));
            Assert.AreEqual(HttpStatusCode.Created, pagamento.StatusCode, await pagamento.Content.ReadAsStringAsync());
        }

        using var prontidaoA = await Fase4Apoio.ProntidaoAsync(caixa, pedidoId);
        using var prontidaoB = await Fase4Apoio.ProntidaoAsync(caixa, pedidoId);
        var respostas = await Task.WhenAll(
            Fase4Apoio.ConfirmarEntregaAsync(caixa, pedidoId, prontidaoA, Guid.NewGuid()),
            Fase4Apoio.ConfirmarEntregaAsync(caixa, pedidoId, prontidaoB, Guid.NewGuid()));

        try
        {
            Assert.AreEqual(1, respostas.Count(resposta => resposta.StatusCode == HttpStatusCode.Created));
            Assert.AreEqual(1, respostas.Count(resposta => resposta.StatusCode == HttpStatusCode.Conflict));
        }
        finally
        {
            foreach (var resposta in respostas)
            {
                resposta.Dispose();
            }
        }

        using (var escopo = fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
            Assert.AreEqual(1, await db.EntregasVeiculo.CountAsync());
            Assert.AreEqual(1, await db.Ocupacoes.CountAsync(ocupacao => ocupacao.FimUtc != null));
            Assert.AreEqual(0, await db.Ocupacoes.CountAsync(ocupacao => ocupacao.FimUtc == null));
        }
    }

    [TestMethod]
    public async Task DisputasEntregaContraBuscaRevogacaoEPagamentoSaoSerializadasSemEfeitoParcial()
    {
        using var fabrica = Fase4Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);
        var (outroCaixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        // Entrega × busca: a busca pode vencer, mas a entrega não pode
        // produzir efeito com versão antiga do pedido.
        var atendimentoBusca = await Fase3Apoio.PrepararAtendimentoAsync(
            administrador,
            caixa,
            vaga: "C-01",
            placa: "BUS1A23");
        var pedidoBusca = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, atendimentoBusca);
        await Fase4Apoio.RegistrarVistoriaConfirmadaAsync(caixa, atendimentoBusca);
        using var caixaBusca = await Fase3Apoio.CaixaAsync(caixa, pedidoBusca);
        using (var pagamentoBusca = await Fase3Apoio.PagarAsync(
                   caixa,
                   pedidoBusca,
                   Fase3Apoio.Saldo(caixaBusca),
                   "pix",
                   Fase3Apoio.RevisaoDaRazao(caixaBusca)))
        {
            Assert.AreEqual(HttpStatusCode.Created, pagamentoBusca.StatusCode);
        }

        using var detalheBusca = await Fase3Apoio.DetalheAsync(caixa, pedidoBusca);
        using var prontidaoBusca = await Fase4Apoio.ProntidaoAsync(caixa, pedidoBusca);
        var disputaBusca = await Task.WhenAll(
            Fase3Apoio.IniciarBuscaAsync(caixa, pedidoBusca, Fase3Apoio.VersaoDoPedido(detalheBusca)),
            Fase4Apoio.ConfirmarEntregaAsync(caixa, pedidoBusca, prontidaoBusca, Guid.NewGuid()));

        try
        {
            var sucessosBusca = disputaBusca.Count(resposta =>
                resposta.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created);
            Assert.AreEqual(1, sucessosBusca);
            Assert.IsTrue(disputaBusca.All(resposta =>
                resposta.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created or HttpStatusCode.Conflict));
        }
        finally
        {
            foreach (var resposta in disputaBusca)
            {
                resposta.Dispose();
            }
        }

        // Entrega × revogação: exatamente uma operação vence; a outra conflita
        // ou encontra a autorização já consumida, sem entrega parcial.
        var atendimentoRevogacao = await Fase3Apoio.PrepararAtendimentoComTarifaPublicadaAsync(
            administrador,
            caixa,
            vaga: "C-02",
            placa: "REV1A23");
        var pedidoRevogacao = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(
            caixa,
            atendimentoRevogacao,
            conferencia: "divergente");
        await Fase4Apoio.RegistrarVistoriaConfirmadaAsync(caixa, atendimentoRevogacao);
        using var detalheRevogacao = await Fase3Apoio.DetalheAsync(caixa, pedidoRevogacao);
        using (var concessao = await Fase3Apoio.ConcederAutorizacaoAsync(
                   administrador,
                   pedidoRevogacao,
                   "documental",
                   "Divergência documental justificada.",
                   Fase3Apoio.VersaoDoPedido(detalheRevogacao)))
        {
            Assert.AreEqual(HttpStatusCode.Created, concessao.StatusCode);
        }

        using var caixaRevogacao = await Fase3Apoio.CaixaAsync(caixa, pedidoRevogacao);
        var autorizacaoRevogacao = caixaRevogacao.RootElement
            .GetProperty("autorizacaoDocumental")
            .GetProperty("id")
            .GetGuid();
        using var prontidaoRevogacao = await Fase4Apoio.ProntidaoAsync(caixa, pedidoRevogacao);
        var disputaRevogacao = await Task.WhenAll(
            Fase3Apoio.RevogarAutorizacaoAsync(
                administrador,
                pedidoRevogacao,
                autorizacaoRevogacao,
                "Revogação concorrente com a entrega."),
            Fase4Apoio.ConfirmarEntregaAsync(caixa, pedidoRevogacao, prontidaoRevogacao, Guid.NewGuid()));

        try
        {
            Assert.AreEqual(1, disputaRevogacao.Count(resposta =>
                resposta.StatusCode is HttpStatusCode.Created));
            Assert.IsTrue(disputaRevogacao.All(resposta =>
                resposta.StatusCode is HttpStatusCode.Created or
                    HttpStatusCode.Conflict or
                    HttpStatusCode.UnprocessableEntity));
        }
        finally
        {
            foreach (var resposta in disputaRevogacao)
            {
                resposta.Dispose();
            }
        }

        using (var escopo = fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
            Assert.IsTrue(await db.EntregasVeiculo.CountAsync() <= 2);
            Assert.IsTrue(await db.ConsumosAutorizacao.CountAsync() <= 2);
            Assert.AreEqual(
                await db.Atendimentos.CountAsync(),
                await db.Ocupacoes.CountAsync());
        }

        // Entrega × acerto: o acerto pode vencer e tornar a concessão
        // financeira obsoleta, ou ocorrer depois da entrega; em ambos os casos
        // não há entrega duplicada nem ocupação reaberta.
        var atendimentoAcerto = await Fase3Apoio.PrepararAtendimentoComTarifaPublicadaAsync(
            administrador,
            caixa,
            vaga: "C-03",
            placa: "PAG1A23");
        var pedidoAcerto = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, atendimentoAcerto);
        await Fase4Apoio.RegistrarVistoriaConfirmadaAsync(caixa, atendimentoAcerto);
        using var detalheAcerto = await Fase3Apoio.DetalheAsync(caixa, pedidoAcerto);
        using (var concessao = await Fase3Apoio.ConcederAutorizacaoAsync(
                   administrador,
                   pedidoAcerto,
                   "financeira",
                   "Saldo excepcional concorrente.",
                   Fase3Apoio.VersaoDoPedido(detalheAcerto)))
        {
            Assert.AreEqual(HttpStatusCode.Created, concessao.StatusCode);
        }

        using var caixaAcerto = await Fase3Apoio.CaixaAsync(caixa, pedidoAcerto);
        using var prontidaoAcerto = await Fase4Apoio.ProntidaoAsync(caixa, pedidoAcerto);
        var disputaAcerto = await Task.WhenAll(
            Fase3Apoio.PagarAsync(
                outroCaixa,
                pedidoAcerto,
                Fase3Apoio.Saldo(caixaAcerto),
                "dinheiro",
                Fase3Apoio.RevisaoDaRazao(caixaAcerto)),
            Fase4Apoio.ConfirmarEntregaAsync(caixa, pedidoAcerto, prontidaoAcerto, Guid.NewGuid()));

        var entregaAcertoStatus = disputaAcerto[1].StatusCode;

        try
        {
            Assert.AreEqual(HttpStatusCode.Created, disputaAcerto[0].StatusCode);
            Assert.IsTrue(entregaAcertoStatus is
                HttpStatusCode.Created or HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict);
        }
        finally
        {
            foreach (var resposta in disputaAcerto)
            {
                resposta.Dispose();
            }
        }

        using (var escopo = fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
            var entregue = entregaAcertoStatus == HttpStatusCode.Created;
            Assert.AreEqual(
                entregue ? 1 : 0,
                await db.EntregasVeiculo.CountAsync(entrega => entrega.PedidoId == pedidoAcerto));
            Assert.AreEqual(
                entregue ? 0 : 1,
                await db.Ocupacoes.CountAsync(ocupacao =>
                    ocupacao.AtendimentoId == atendimentoAcerto && ocupacao.FimUtc == null));
            Assert.IsTrue(await db.LancamentosFinanceiros.CountAsync() >= 1);
        }
    }

    [TestMethod]
    public async Task FalhaDeAuditoriaNaVistoriaDesfazRegistroEReservaIdempotente()
    {
        using var fabrica = Fase4Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);
        var (manobrista, _) = await Fase2Apoio.EntrarManobristaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa);
        await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, atendimentoId);

        SqlTeste.Executar(
            fabrica.NomeBanco,
            "CREATE TRIGGER [TesteFalhaAuditoriaVistoria] ON [EventosAuditoriaNegocio] AFTER INSERT AS " +
            "BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM inserted WHERE [Tipo] = 'vistoria_saida_registrada') " +
            "THROW 51000, N'falha simulada de auditoria', 1; END");

        var versao = await Fase4Apoio.VersaoAtendimentoAsync(manobrista, atendimentoId);
        using var resposta = await Fase4Apoio.RegistrarVistoriaAsync(
            manobrista,
            atendimentoId,
            "Vistoria que deve ser desfeita.",
            versao);

        Assert.AreEqual(HttpStatusCode.InternalServerError, resposta.StatusCode);

        using (var escopo = fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
            Assert.AreEqual(0, await db.VistoriasSaida.CountAsync());
            Assert.AreEqual(0, await db.ResultadosIdempotentes.CountAsync(
                resultado => resultado.Operacao == "attendance.exit-inspection"));
            Assert.AreEqual(0, await db.EventosAtendimento.CountAsync(
                evento => evento.Tipo == "vistoria_saida_registrada"));
        }
    }

    [TestMethod]
    public async Task FalhasForcadasNaEntregaNaoDeixamEfeitosParciais()
    {
        (string Nome, string Sql)[] cenarios =
        [
            (
                "efeito",
                "CREATE TRIGGER [TesteFalhaEntrega] ON [EntregasVeiculo] AFTER INSERT AS " +
                "BEGIN SET NOCOUNT ON; THROW 51001, N'falha simulada de efeito', 1; END"),
            (
                "auditoria",
                "CREATE TRIGGER [TesteFalhaAuditoriaEntrega] ON [EventosAuditoriaNegocio] AFTER INSERT AS " +
                "BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM inserted WHERE [Tipo] = 'entrega_confirmada') " +
                "THROW 51002, N'falha simulada de auditoria', 1; END"),
            (
                "recibo",
                "CREATE TRIGGER [TesteFalhaRecibo] ON [ResultadosIdempotentes] AFTER UPDATE AS " +
                "BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM inserted WHERE [Operacao] = 'withdrawal.delivery' " +
                "AND [ResultadoJson] IS NOT NULL) THROW 51003, N'falha simulada de recibo', 1; END")
        ];

        foreach (var cenario in cenarios)
        {
            using var fabrica = Fase4Apoio.NovaFabrica();
            await fabrica.ProvisionarContasAsync();
            var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
            var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

            var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa);
            var pedidoId = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, atendimentoId);
            await Fase4Apoio.RegistrarVistoriaConfirmadaAsync(caixa, atendimentoId);

            using (var caixaInicial = await Fase3Apoio.CaixaAsync(caixa, pedidoId))
            {
                using var pagamento = await Fase3Apoio.PagarAsync(
                    caixa,
                    pedidoId,
                    Fase3Apoio.Saldo(caixaInicial),
                    "pix",
                    Fase3Apoio.RevisaoDaRazao(caixaInicial));
                Assert.AreEqual(HttpStatusCode.Created, pagamento.StatusCode);
            }

            SqlTeste.Executar(fabrica.NomeBanco, cenario.Sql);

            using var prontidao = await Fase4Apoio.ProntidaoAsync(caixa, pedidoId);
            using var resposta = await Fase4Apoio.ConfirmarEntregaAsync(caixa, pedidoId, prontidao);
            Assert.AreEqual(
                HttpStatusCode.InternalServerError,
                resposta.StatusCode,
                $"Cenário de falha forçada: {cenario.Nome}");

            using (var escopo = fabrica.Services.CreateScope())
            {
                var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
                Assert.AreEqual(0, await db.EntregasVeiculo.CountAsync(), cenario.Nome);
                Assert.AreEqual(0, await db.ConsumosAutorizacao.CountAsync(), cenario.Nome);
                Assert.AreEqual(
                    "aguardando",
                    (await db.PedidosRetirada.AsNoTracking().SingleAsync()).Estado,
                    cenario.Nome);
                Assert.AreEqual(
                    "aguardando",
                    (await db.Atendimentos.AsNoTracking().SingleAsync()).Estado,
                    cenario.Nome);
                Assert.AreEqual(
                    1,
                    await db.Ocupacoes.CountAsync(ocupacao => ocupacao.FimUtc == null),
                    cenario.Nome);
                Assert.AreEqual(
                    0,
                    await db.ResultadosIdempotentes.CountAsync(
                        resultado => resultado.Operacao == "withdrawal.delivery"),
                    cenario.Nome);
                Assert.AreEqual(
                    0,
                    await db.EventosAtendimento.CountAsync(
                        evento => evento.Tipo == "entrega_confirmada" || evento.Tipo == "vaga_liberada"),
                    cenario.Nome);
            }
        }
    }

    [TestMethod]
    public async Task MesmaChaveConcorrenteRetornaUmUnicoReciboENaoDuplicaEfeitos()
    {
        using var fabrica = Fase4Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa);
        var pedidoId = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, atendimentoId);
        await Fase4Apoio.RegistrarVistoriaConfirmadaAsync(caixa, atendimentoId);
        using (var caixaInicial = await Fase3Apoio.CaixaAsync(caixa, pedidoId))
        {
            using var pagamento = await Fase3Apoio.PagarAsync(
                caixa,
                pedidoId,
                Fase3Apoio.Saldo(caixaInicial),
                "pix",
                Fase3Apoio.RevisaoDaRazao(caixaInicial));
            Assert.AreEqual(HttpStatusCode.Created, pagamento.StatusCode);
        }

        using var prontidaoA = await Fase4Apoio.ProntidaoAsync(caixa, pedidoId);
        using var prontidaoB = await Fase4Apoio.ProntidaoAsync(caixa, pedidoId);
        var chave = Guid.NewGuid();
        var respostas = await Task.WhenAll(
            Fase4Apoio.ConfirmarEntregaAsync(caixa, pedidoId, prontidaoA, chave),
            Fase4Apoio.ConfirmarEntregaAsync(caixa, pedidoId, prontidaoB, chave));

        try
        {
            Assert.IsTrue(respostas.All(resposta => resposta.StatusCode == HttpStatusCode.Created));
            var ids = new List<Guid>();
            foreach (var resposta in respostas)
            {
                using var documento = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
                ids.Add(documento.RootElement.GetProperty("entregaId").GetGuid());
            }

            Assert.AreEqual(1, ids.Distinct().Count());
        }
        finally
        {
            foreach (var resposta in respostas)
            {
                resposta.Dispose();
            }
        }

        using (var escopo = fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
            Assert.AreEqual(1, await db.EntregasVeiculo.CountAsync());
            Assert.AreEqual(1, await db.Ocupacoes.CountAsync(ocupacao => ocupacao.FimUtc != null));
        }
    }

    [TestMethod]
    public async Task SaldoZeradoPorDescontoNaoExigePagamentoNemAutorizacaoFinanceira()
    {
        using var fabrica = Fase4Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa);
        var pedidoId = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, atendimentoId);
        await Fase4Apoio.RegistrarVistoriaConfirmadaAsync(caixa, atendimentoId);
        using var caixaInicial = await Fase3Apoio.CaixaAsync(caixa, pedidoId);
        using (var desconto = await Fase3Apoio.DescontarAsync(
                   administrador,
                   pedidoId,
                   Fase3Apoio.Saldo(caixaInicial),
                   "Desconto integral de demonstração.",
                   Fase3Apoio.RevisaoDaRazao(caixaInicial)))
        {
            Assert.AreEqual(HttpStatusCode.Created, desconto.StatusCode, await desconto.Content.ReadAsStringAsync());
        }

        using var prontidao = await Fase4Apoio.ProntidaoAsync(caixa, pedidoId);
        Assert.AreEqual(0m, prontidao.RootElement.GetProperty("saldo").GetDecimal());
        Assert.IsTrue(prontidao.RootElement.GetProperty("pronta").GetBoolean());

        using var entrega = await Fase4Apoio.ConfirmarEntregaAsync(caixa, pedidoId, prontidao);
        Assert.AreEqual(HttpStatusCode.Created, entrega.StatusCode, await entrega.Content.ReadAsStringAsync());

        using var caixaFinal = await Fase3Apoio.CaixaAsync(caixa, pedidoId);
        Assert.AreEqual(0m, Fase3Apoio.Saldo(caixaFinal));
        Assert.AreEqual(0, caixaFinal.RootElement.GetProperty("pagamentos").GetDecimal());
    }

    [TestMethod]
    public async Task ConcessaoSubstituidaNaoEhConsumidaEApenasACorrenteEhUsada()
    {
        using var fabrica = Fase4Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa);
        var pedidoId = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, atendimentoId);
        await Fase4Apoio.RegistrarVistoriaConfirmadaAsync(caixa, atendimentoId);
        using var detalhe = await Fase3Apoio.DetalheAsync(caixa, pedidoId);

        Guid primeira;
        using (var concessao = await Fase3Apoio.ConcederAutorizacaoAsync(
                   administrador,
                   pedidoId,
                   "financeira",
                   "Primeira concessão, que será substituída.",
                   Fase3Apoio.VersaoDoPedido(detalhe)))
        {
            Assert.AreEqual(HttpStatusCode.Created, concessao.StatusCode);
            using var documento = JsonDocument.Parse(await concessao.Content.ReadAsStringAsync());
            primeira = documento.RootElement.GetProperty("autorizacaoId").GetGuid();
        }

        Guid segunda;
        using (var concessao = await Fase3Apoio.ConcederAutorizacaoAsync(
                   administrador,
                   pedidoId,
                   "financeira",
                   "Segunda concessão corrente.",
                   Fase3Apoio.VersaoDoPedido(detalhe)))
        {
            Assert.AreEqual(HttpStatusCode.Created, concessao.StatusCode);
            using var documento = JsonDocument.Parse(await concessao.Content.ReadAsStringAsync());
            segunda = documento.RootElement.GetProperty("autorizacaoId").GetGuid();
        }

        using var prontidao = await Fase4Apoio.ProntidaoAsync(caixa, pedidoId);
        Assert.IsTrue(prontidao.RootElement.GetProperty("pronta").GetBoolean());
        using var entrega = await Fase4Apoio.ConfirmarEntregaAsync(caixa, pedidoId, prontidao);
        Assert.AreEqual(HttpStatusCode.Created, entrega.StatusCode, await entrega.Content.ReadAsStringAsync());

        using (var escopo = fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
            var consumo = await db.ConsumosAutorizacao.AsNoTracking().SingleAsync();
            Assert.AreEqual(segunda, consumo.ConcessaoId);
            Assert.AreNotEqual(primeira, consumo.ConcessaoId);
            Assert.AreEqual(2, await db.ConcessoesAutorizacao.CountAsync());
        }

        using var caixaFinal = await Fase3Apoio.CaixaAsync(caixa, pedidoId);
        Assert.AreEqual(
            segunda,
            caixaFinal.RootElement.GetProperty("autorizacaoFinanceira").GetProperty("id").GetGuid());
        Assert.AreEqual(
            "consumida",
            caixaFinal.RootElement.GetProperty("autorizacaoFinanceira").GetProperty("vigencia").GetString());
    }

    [TestMethod]
    public async Task ManobristaRegistraVistoriaSemAcessarProntidaoConfirmacaoOuFinancas()
    {
        using var fabrica = Fase4Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);
        var (manobrista, _) = await Fase2Apoio.EntrarManobristaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa);
        var pedidoId = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, atendimentoId);
        await Fase4Apoio.RegistrarVistoriaConfirmadaAsync(manobrista, atendimentoId);

        using var prontidao = await manobrista.GetAsync(
            $"/api/v1/withdrawals/{pedidoId}/delivery-readiness");
        Assert.AreEqual(HttpStatusCode.Forbidden, prontidao.StatusCode);

        using var entrega = await Fase4Apoio.EnviarComChaveAsync(
            manobrista,
            HttpMethod.Post,
            $"/api/v1/withdrawals/{pedidoId}/delivery",
            new { confirmacao = true },
            Guid.NewGuid());
        Assert.AreEqual(HttpStatusCode.Forbidden, entrega.StatusCode);

        using var fila = await Fase3Apoio.FilaAsync(manobrista);
        var item = fila.RootElement.GetProperty("items")[0];
        Assert.IsTrue(item.GetProperty("vistoriaSaidaRegistrada").GetBoolean());
        Assert.IsFalse(item.TryGetProperty("saldo", out _));
        Assert.IsFalse(item.TryGetProperty("autorizacaoFinanceira", out _));

        using var historico = JsonDocument.Parse(
            await (await manobrista.GetAsync($"/api/v1/attendances/{atendimentoId}/history?page=1&pageSize=50"))
                .Content.ReadAsStringAsync());
        var tipos = historico.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(registro => registro.GetProperty("tipo").GetString() ?? string.Empty)
            .ToArray();
        CollectionAssert.Contains(tipos, "vistoria_saida_registrada");
        CollectionAssert.DoesNotContain(tipos, "autorizacao_consumida");
    }
}
