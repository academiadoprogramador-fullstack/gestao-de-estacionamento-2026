using System.Net;
using System.Text.Json;
using Estacionamento.Infrastructure.Persistencia;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Estacionamento.IntegrationTests;

[TestClass]
public sealed class RetiradaCaixaTestes
{
    [TestMethod]
    public async Task AcertoIntegralRecusaParcialExcedenteZeradoEOutroPerfil()
    {
        using var fabrica = Fase3Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);
        var (manobrista, _) = await Fase2Apoio.EntrarManobristaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa, tarifa: 60.00m);
        var pedidoId = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, atendimentoId);

        using (var caixaInicial = await Fase3Apoio.CaixaAsync(caixa, pedidoId))
        {
            Assert.AreEqual(1, Fase3Apoio.RevisaoDaRazao(caixaInicial));
            Assert.AreEqual(60.00m, Fase3Apoio.Saldo(caixaInicial));
        }

        using var parcial = await Fase3Apoio.PagarAsync(caixa, pedidoId, 59.00m, "pix", 1);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, parcial.StatusCode);
        Assert.AreEqual("payment_must_equal_balance", await Fase3Apoio.CodigoAsync(parcial));

        using var excedente = await Fase3Apoio.PagarAsync(caixa, pedidoId, 61.00m, "pix", 1);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, excedente.StatusCode);
        Assert.AreEqual("payment_must_equal_balance", await Fase3Apoio.CodigoAsync(excedente));

        using var formaInvalida = await Fase3Apoio.PagarAsync(caixa, pedidoId, 60.00m, "boleto", 1);
        Assert.AreEqual(HttpStatusCode.BadRequest, formaInvalida.StatusCode);
        Assert.AreEqual("payment_invalid_method", await Fase3Apoio.CodigoAsync(formaInvalida));

        using var negado = await Fase3Apoio.PagarAsync(manobrista, pedidoId, 60.00m, "pix", 1);
        Assert.AreEqual(HttpStatusCode.Forbidden, negado.StatusCode);

        var chave = Guid.NewGuid();
        using var pago = await Fase3Apoio.PagarAsync(caixa, pedidoId, 60.00m, "pix", 1, chave);
        Assert.AreEqual(HttpStatusCode.Created, pago.StatusCode, await pago.Content.ReadAsStringAsync());
        using var pagoJson = await Fase2Apoio.LerJsonAsync(pago);
        Assert.AreEqual(0m, pagoJson.RootElement.GetProperty("saldo").GetDecimal());
        Assert.AreEqual(2, pagoJson.RootElement.GetProperty("revisaoRazao").GetInt32());

        using var replay = await Fase3Apoio.PagarAsync(caixa, pedidoId, 60.00m, "pix", 1, chave);
        Assert.AreEqual(HttpStatusCode.Created, replay.StatusCode, await replay.Content.ReadAsStringAsync());
        using var replayJson = await Fase2Apoio.LerJsonAsync(replay);
        Assert.AreEqual(
            pagoJson.RootElement.GetProperty("lancamentoId").GetGuid(),
            replayJson.RootElement.GetProperty("lancamentoId").GetGuid());

        using var replayDivergente = await Fase3Apoio.PagarAsync(caixa, pedidoId, 60.00m, "dinheiro", 1, chave);
        Assert.AreEqual(HttpStatusCode.Conflict, replayDivergente.StatusCode);
        Assert.AreEqual("idempotency_conflict", await Fase3Apoio.CodigoAsync(replayDivergente));

        using var repetido = await Fase3Apoio.PagarAsync(caixa, pedidoId, 60.00m, "pix", 2);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, repetido.StatusCode);
        Assert.AreEqual("balance_already_settled", await Fase3Apoio.CodigoAsync(repetido));

        using var zeroInvalido = await Fase3Apoio.PagarAsync(caixa, pedidoId, 0m, "pix", 2);
        Assert.AreEqual(HttpStatusCode.BadRequest, zeroInvalido.StatusCode);

        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
        Assert.AreEqual(1, await db.LancamentosFinanceiros.CountAsync(lancamento =>
            lancamento.AtendimentoId == atendimentoId &&
            lancamento.Tipo == "pagamento"));
    }

    [TestMethod]
    public async Task TarifaPublicadaDepoisNaoAlteraTotalFixadoNoPedido()
    {
        using var fabrica = Fase3Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa, tarifa: 20.00m);
        var pedidoId = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, atendimentoId);

        using var corrente = JsonDocument.Parse(
            await (await administrador.GetAsync("/api/v1/daily-rates/current"))
                .Content.ReadAsStringAsync());
        await Fase2Apoio.PublicarTarifaAsync(
            administrador,
            50.00m,
            corrente.RootElement.GetProperty("versaoBase").GetInt32());

        using var caixaApos = await Fase3Apoio.CaixaAsync(caixa, pedidoId);
        Assert.AreEqual(20.00m, caixaApos.RootElement.GetProperty("totalOriginal").GetDecimal());
        Assert.AreEqual(20.00m, caixaApos.RootElement.GetProperty("tarifaValorSnapshot").GetDecimal());
        Assert.AreEqual(1, caixaApos.RootElement.GetProperty("diarias").GetInt32());
    }

    [TestMethod]
    public async Task DescontoRespeitaLimitesPreservaCobrancaEProtegeJustificativa()
    {
        using var fabrica = Fase3Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);
        var (manobrista, _) = await Fase2Apoio.EntrarManobristaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa, tarifa: 60.00m);
        var pedidoId = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, atendimentoId);

        using var negado = await Fase3Apoio.DescontarAsync(
            caixa,
            pedidoId,
            10.00m,
            "Tentativa de caixa.",
            1);
        Assert.AreEqual(HttpStatusCode.Forbidden, negado.StatusCode);

        using var semJustificativa = await Fase3Apoio.DescontarAsync(
            administrador,
            pedidoId,
            10.00m,
            "   ",
            1);
        Assert.AreEqual(HttpStatusCode.BadRequest, semJustificativa.StatusCode);
        Assert.AreEqual("discount_requires_justification", await Fase3Apoio.CodigoAsync(semJustificativa));

        var chaveDesconto = Guid.NewGuid();
        using var desconto = await Fase3Apoio.DescontarAsync(
            administrador,
            pedidoId,
            10.00m,
            "Cortesia administrativa sintética.",
            1,
            chaveDesconto);
        Assert.AreEqual(HttpStatusCode.Created, desconto.StatusCode, await desconto.Content.ReadAsStringAsync());

        using (var aposDesconto = await Fase3Apoio.CaixaAsync(caixa, pedidoId))
        {
            Assert.AreEqual(60.00m, aposDesconto.RootElement.GetProperty("totalOriginal").GetDecimal());
            Assert.AreEqual(50.00m, aposDesconto.RootElement.GetProperty("cobrado").GetDecimal());
            Assert.AreEqual(50.00m, aposDesconto.RootElement.GetProperty("saldo").GetDecimal());
            Assert.AreEqual(2, Fase3Apoio.RevisaoDaRazao(aposDesconto));
        }

        using var limite = await Fase3Apoio.DescontarAsync(
            administrador,
            pedidoId,
            60.00m,
            "Acima do valor descontável.",
            2);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, limite.StatusCode);
        Assert.AreEqual("discount_exceeds_balance", await Fase3Apoio.CodigoAsync(limite));

        using var replayDesconto = await Fase3Apoio.DescontarAsync(
            administrador,
            pedidoId,
            10.00m,
            "Cortesia administrativa sintética.",
            1,
            chaveDesconto);
        Assert.AreEqual(HttpStatusCode.Created, replayDesconto.StatusCode);
        using var replayJson = await Fase2Apoio.LerJsonAsync(replayDesconto);
        Assert.AreEqual(
            (await Fase2Apoio.LerJsonAsync(desconto)).RootElement.GetProperty("lancamentoId").GetGuid(),
            replayJson.RootElement.GetProperty("lancamentoId").GetGuid());

        using var pagamento = await Fase3Apoio.PagarAsync(caixa, pedidoId, 50.00m, "cartao", 2);
        Assert.AreEqual(HttpStatusCode.Created, pagamento.StatusCode, await pagamento.Content.ReadAsStringAsync());

        using var descontoAposPagamento = await Fase3Apoio.DescontarAsync(
            administrador,
            pedidoId,
            10.00m,
            "Desconto após pagamento.",
            3);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, descontoAposPagamento.StatusCode);
        Assert.AreEqual("discount_requires_no_payment", await Fase3Apoio.CodigoAsync(descontoAposPagamento));

        var lancamentoDesconto = await Fase3Apoio.ObterLancamentoIdAsync(caixa, pedidoId, "desconto");
        using var caixaNegado = await Fase3Apoio.LerJustificativaLancamentoAsync(
            caixa,
            pedidoId,
            lancamentoDesconto);
        Assert.AreEqual(HttpStatusCode.Forbidden, caixaNegado.StatusCode);

        using var manobristaNegado = await Fase3Apoio.LerJustificativaLancamentoAsync(
            manobrista,
            pedidoId,
            lancamentoDesconto);
        Assert.AreEqual(HttpStatusCode.Forbidden, manobristaNegado.StatusCode);

        using var leitura = await Fase3Apoio.LerJustificativaLancamentoAsync(
            administrador,
            pedidoId,
            lancamentoDesconto);
        Assert.AreEqual(HttpStatusCode.OK, leitura.StatusCode, await leitura.Content.ReadAsStringAsync());
        using var leituraJson = await Fase2Apoio.LerJsonAsync(leitura);
        Assert.AreEqual(
            "Cortesia administrativa sintética.",
            leituraJson.RootElement.GetProperty("conteudo").GetString());

        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
        Assert.IsTrue(await db.EventosAuditoriaNegocio.AnyAsync(evento =>
            evento.Tipo == "justificativa_lida" && evento.AlvoId == lancamentoDesconto));
        Assert.AreEqual(1, await db.LancamentosFinanceiros.CountAsync(lancamento =>
            lancamento.Tipo == "desconto"));

        var resultadoPersistido = await db.ResultadosIdempotentes
            .AsNoTracking()
            .SingleAsync(resultado => resultado.Operacao == "cashier.discount");
        Assert.IsNotNull(resultadoPersistido.ResultadoJson);
        Assert.IsFalse(resultadoPersistido.ResultadoJson.Contains("Cortesia", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(resultadoPersistido.ResultadoJson.Contains("justificativa", StringComparison.OrdinalIgnoreCase));

        var lancamentoArmazenado = await db.LancamentosFinanceiros
            .AsNoTracking()
            .SingleAsync(lancamento => lancamento.Id == lancamentoDesconto);
        var envelopeAdulterado = lancamentoArmazenado.JustificativaCifrada!.ToArray();
        envelopeAdulterado[^1] ^= 0xFF;
        SqlTeste.Executar(
            fabrica.NomeBanco,
            $"UPDATE [LancamentosFinanceiros] SET [JustificativaCifrada] = 0x{Convert.ToHexString(envelopeAdulterado)} WHERE [Id] = '{lancamentoDesconto}';");
        db.ChangeTracker.Clear();

        using var adulterada = await Fase3Apoio.LerJustificativaLancamentoAsync(
            administrador,
            pedidoId,
            lancamentoDesconto);
        Assert.AreEqual(HttpStatusCode.InternalServerError, adulterada.StatusCode);
        Assert.AreEqual("field_protection_failure", await Fase3Apoio.CodigoAsync(adulterada));
    }

    [TestMethod]
    public async Task EstornoIntegralUnicoReabreSaldoEPreservaOPagamentoOriginal()
    {
        using var fabrica = Fase3Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa, tarifa: 60.00m);
        var pedidoId = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, atendimentoId);
        using var pagamento = await Fase3Apoio.PagarAsync(caixa, pedidoId, 60.00m, "dinheiro", 1);
        Assert.AreEqual(HttpStatusCode.Created, pagamento.StatusCode, await pagamento.Content.ReadAsStringAsync());
        var pagamentoId = await Fase3Apoio.ObterPagamentoIdAsync(caixa, pedidoId);

        using var parcial = await Fase3Apoio.EstornarAsync(
            administrador,
            pedidoId,
            pagamentoId,
            50.00m,
            "Tentativa parcial.",
            2);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, parcial.StatusCode);
        Assert.AreEqual("refund_must_be_full", await Fase3Apoio.CodigoAsync(parcial));

        using var caixaNegado = await Fase3Apoio.EstornarAsync(
            caixa,
            pedidoId,
            pagamentoId,
            60.00m,
            "Tentativa de caixa.",
            2);
        Assert.AreEqual(HttpStatusCode.Forbidden, caixaNegado.StatusCode);

        var chave = Guid.NewGuid();
        using var estorno = await Fase3Apoio.EstornarAsync(
            administrador,
            pedidoId,
            pagamentoId,
            60.00m,
            "Cliente desistiu da retirada.",
            2,
            chave);
        Assert.AreEqual(HttpStatusCode.Created, estorno.StatusCode, await estorno.Content.ReadAsStringAsync());
        using var estornoJson = await Fase2Apoio.LerJsonAsync(estorno);
        Assert.AreEqual(60.00m, estornoJson.RootElement.GetProperty("saldo").GetDecimal());
        Assert.AreEqual(3, estornoJson.RootElement.GetProperty("revisaoRazao").GetInt32());

        using (var aposEstorno = await Fase3Apoio.CaixaAsync(caixa, pedidoId))
        {
            Assert.AreEqual(60.00m, aposEstorno.RootElement.GetProperty("cobrado").GetDecimal());
            Assert.AreEqual(0m, aposEstorno.RootElement.GetProperty("pagoLiquido").GetDecimal());
            Assert.AreEqual(60.00m, Fase3Apoio.Saldo(aposEstorno));
            Assert.AreEqual(
                2,
                aposEstorno.RootElement.GetProperty("lancamentos").GetArrayLength());
        }

        using var replay = await Fase3Apoio.EstornarAsync(
            administrador,
            pedidoId,
            pagamentoId,
            60.00m,
            "Cliente desistiu da retirada.",
            2,
            chave);
        Assert.AreEqual(HttpStatusCode.Created, replay.StatusCode);
        using var replayJson = await Fase2Apoio.LerJsonAsync(replay);
        Assert.AreEqual(
            estornoJson.RootElement.GetProperty("lancamentoId").GetGuid(),
            replayJson.RootElement.GetProperty("lancamentoId").GetGuid());

        using var segundo = await Fase3Apoio.EstornarAsync(
            administrador,
            pedidoId,
            pagamentoId,
            60.00m,
            "Segundo estorno.",
            3);
        Assert.AreEqual(HttpStatusCode.Conflict, segundo.StatusCode);
        Assert.AreEqual("payment_already_refunded", await Fase3Apoio.CodigoAsync(segundo));

        using var novoAcerto = await Fase3Apoio.PagarAsync(caixa, pedidoId, 60.00m, "pix", 3);
        Assert.AreEqual(HttpStatusCode.Created, novoAcerto.StatusCode, await novoAcerto.Content.ReadAsStringAsync());

        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
        Assert.AreEqual(1, await db.LancamentosFinanceiros.CountAsync(lancamento =>
            lancamento.AtendimentoId == atendimentoId && lancamento.Tipo == "estorno"));
        Assert.AreEqual(2, await db.LancamentosFinanceiros.CountAsync(lancamento =>
            lancamento.AtendimentoId == atendimentoId && lancamento.Tipo == "pagamento"));
    }

    [TestMethod]
    public async Task DoisPagamentosConcorrentesConfirmamUmUnicoLancamento()
    {
        using var fabrica = Fase3Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixaA, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);
        var (caixaB, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixaA, tarifa: 60.00m);
        var pedidoId = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixaA, atendimentoId);

        var respostas = await Task.WhenAll(
            Fase3Apoio.PagarAsync(caixaA, pedidoId, 60.00m, "pix", 1),
            Fase3Apoio.PagarAsync(caixaB, pedidoId, 60.00m, "dinheiro", 1));

        Assert.AreEqual(1, respostas.Count(resposta => resposta.StatusCode == HttpStatusCode.Created));
        Assert.AreEqual(1, respostas.Count(resposta => resposta.StatusCode == HttpStatusCode.Conflict));
        var conflito = respostas.Single(resposta => resposta.StatusCode == HttpStatusCode.Conflict);
        Assert.AreEqual("ledger_revision_stale", await Fase3Apoio.CodigoAsync(conflito));

        foreach (var resposta in respostas)
        {
            resposta.Dispose();
        }

        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
        Assert.AreEqual(1, await db.LancamentosFinanceiros.CountAsync(lancamento =>
            lancamento.AtendimentoId == atendimentoId && lancamento.Tipo == "pagamento"));

        using var caixaFinal = await Fase3Apoio.CaixaAsync(caixaA, pedidoId);
        Assert.AreEqual(0m, Fase3Apoio.Saldo(caixaFinal));
    }

    [TestMethod]
    public async Task DescontoConcorrenteComPagamentoConfirmaApenasUmEfeito()
    {
        using var fabrica = Fase3Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa, tarifa: 60.00m);
        var pedidoId = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, atendimentoId);

        var respostas = await Task.WhenAll(
            Fase3Apoio.DescontarAsync(administrador, pedidoId, 10.00m, "Desconto concorrente.", 1),
            Fase3Apoio.PagarAsync(caixa, pedidoId, 60.00m, "pix", 1));

        Assert.AreEqual(
            1,
            respostas.Count(resposta =>
                resposta.StatusCode is HttpStatusCode.Created));
        Assert.AreEqual(
            1,
            respostas.Count(resposta => resposta.StatusCode == HttpStatusCode.Conflict));

        foreach (var resposta in respostas)
        {
            resposta.Dispose();
        }

        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
        Assert.AreEqual(
            1,
            await db.LancamentosFinanceiros.CountAsync(lancamento =>
                lancamento.AtendimentoId == atendimentoId));

        using var caixaFinal = await Fase3Apoio.CaixaAsync(caixa, pedidoId);
        var composicaoValida =
            Fase3Apoio.Saldo(caixaFinal) ==
            caixaFinal.RootElement.GetProperty("cobrado").GetDecimal() -
            caixaFinal.RootElement.GetProperty("pagoLiquido").GetDecimal();
        Assert.IsTrue(composicaoValida);
    }

    [TestMethod]
    public async Task FalhaDeAuditoriaNoDescontoReverteLancamentoERevisao()
    {
        using var fabrica = Fase3Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa, tarifa: 60.00m);
        var pedidoId = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, atendimentoId);

        using var fabricaComAuditoriaQueFalha = new FabricaApiTeste(
                nomeBancoExistente: fabrica.NomeBanco,
                criarBanco: false,
                removerBancoAoDispor: false)
            .WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<Estacionamento.Application.Auditoria.IAuditoriaNegocio>();
                    services.AddScoped<Estacionamento.Application.Auditoria.IAuditoriaNegocio, AuditoriaQueFalha>();
                }));

        using var cliente = fabricaComAuditoriaQueFalha.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("http://localhost")
            });

        var login = await ClienteSessao.LoginAdministradorAsync(cliente);
        ClienteSessao.UsarBearer(cliente, login.TokenAcesso);

        using var resposta = await Fase3Apoio.DescontarAsync(
            cliente,
            pedidoId,
            10.00m,
            "Justificativa com falha de auditoria.",
            1);
        Assert.AreEqual(HttpStatusCode.InternalServerError, resposta.StatusCode);

        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
        Assert.AreEqual(0, await db.LancamentosFinanceiros.CountAsync(lancamento =>
            lancamento.AtendimentoId == atendimentoId &&
            lancamento.Tipo == "desconto"));
        Assert.AreEqual(
            1,
            (await db.RazoesAtendimento.AsNoTracking()
                .SingleAsync(razao => razao.AtendimentoId == atendimentoId)).Revisao);
    }

    [TestMethod]
    public async Task AcertoPosEntregaPorFixtureNaoReabreAtendimentoOcupacaoOuVaga()
    {
        using var fabrica = Fase3Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa, tarifa: 60.00m);
        var pedidoId = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, atendimentoId);

        SqlTeste.Executar(
            fabrica.NomeBanco,
            $"UPDATE [Atendimentos] SET [Estado] = 'entregue' WHERE [Id] = '{atendimentoId}'; " +
            $"UPDATE [PedidosRetirada] SET [Estado] = 'entregue' WHERE [AtendimentoId] = '{atendimentoId}'; " +
            $"UPDATE [Ocupacoes] SET [FimUtc] = SYSDATETIMEOFFSET() WHERE [AtendimentoId] = '{atendimentoId}' AND [FimUtc] IS NULL;");

        using (var caixaFixture = await Fase3Apoio.CaixaAsync(caixa, pedidoId))
        {
            Assert.AreEqual(60.00m, Fase3Apoio.Saldo(caixaFixture));
        }

        using var detalheAtendimento = JsonDocument.Parse(
            await (await caixa.GetAsync($"/api/v1/attendances/{atendimentoId}"))
                .Content.ReadAsStringAsync());
        Assert.AreEqual(
            "entregue",
            detalheAtendimento.RootElement.GetProperty("retirada").GetProperty("estado").GetString());

        using var pagamento = await Fase3Apoio.PagarAsync(caixa, pedidoId, 60.00m, "pix", 1);
        Assert.AreEqual(HttpStatusCode.Created, pagamento.StatusCode, await pagamento.Content.ReadAsStringAsync());

        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
        var atendimento = await db.Atendimentos.AsNoTracking()
            .SingleAsync(registro => registro.Id == atendimentoId);
        Assert.AreEqual("entregue", atendimento.Estado);
        Assert.AreEqual(0, await db.Ocupacoes.CountAsync(ocupacao =>
            ocupacao.AtendimentoId == atendimentoId && ocupacao.FimUtc == null));
        Assert.AreEqual(1, await db.Ocupacoes.CountAsync(ocupacao =>
            ocupacao.AtendimentoId == atendimentoId));
    }

    private sealed class AuditoriaQueFalha : Estacionamento.Application.Auditoria.IAuditoriaNegocio
    {
        public void Adicionar(Estacionamento.Application.Auditoria.EventoAuditoriaNegocio evento) =>
            throw new InvalidOperationException("Falha de auditoria simulada.");

        public Task RegistrarAsync(
            Estacionamento.Application.Auditoria.EventoAuditoriaNegocio evento,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Falha de auditoria simulada.");
    }
}
