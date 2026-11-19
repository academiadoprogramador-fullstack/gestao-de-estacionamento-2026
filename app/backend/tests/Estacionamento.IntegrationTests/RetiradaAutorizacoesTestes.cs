using System.Net;
using Estacionamento.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Estacionamento.IntegrationTests;

[TestClass]
public sealed class RetiradaAutorizacoesTestes
{
    [TestMethod]
    public async Task ConcessaoVigenciaRevogacaoSubstituicaoEObsolescenciaFinanceira()
    {
        using var fabrica = Fase3Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa, tarifa: 60.00m);
        var pedidoId = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(
            caixa,
            atendimentoId,
            conferencia: "divergente");

        using var detalhe = await Fase3Apoio.DetalheAsync(caixa, pedidoId);
        var versao = Fase3Apoio.VersaoDoPedido(detalhe);

        using var semJustificativa = await Fase3Apoio.ConcederAutorizacaoAsync(
            administrador,
            pedidoId,
            "documental",
            "   ",
            versao);
        Assert.AreEqual(HttpStatusCode.BadRequest, semJustificativa.StatusCode);
        Assert.AreEqual("authorization_requires_justification", await Fase3Apoio.CodigoAsync(semJustificativa));

        using var tipoInvalido = await Fase3Apoio.ConcederAutorizacaoAsync(
            administrador,
            pedidoId,
            "terceiro",
            "Justificativa sintética.",
            versao);
        Assert.AreEqual(HttpStatusCode.BadRequest, tipoInvalido.StatusCode);
        Assert.AreEqual("authorization_invalid_type", await Fase3Apoio.CodigoAsync(tipoInvalido));

        using var caixaNegado = await Fase3Apoio.ConcederAutorizacaoAsync(
            caixa,
            pedidoId,
            "documental",
            "Justificativa sintética.",
            versao);
        Assert.AreEqual(HttpStatusCode.Forbidden, caixaNegado.StatusCode);

        using var concessao = await Fase3Apoio.ConcederAutorizacaoAsync(
            administrador,
            pedidoId,
            "documental",
            "Divergência aceita após conferência presencial.",
            versao);
        Assert.AreEqual(HttpStatusCode.Created, concessao.StatusCode, await concessao.Content.ReadAsStringAsync());
        using var concessaoJson = await Fase2Apoio.LerJsonAsync(concessao);
        var primeiraAutorizacao = concessaoJson.RootElement.GetProperty("autorizacaoId").GetGuid();
        Assert.AreEqual("vigente", concessaoJson.RootElement.GetProperty("vigencia").GetString());

        using (var aposConcessao = await Fase3Apoio.CaixaAsync(caixa, pedidoId))
        {
            Assert.AreEqual(60.00m, Fase3Apoio.Saldo(aposConcessao));
            Assert.AreEqual(1, Fase3Apoio.RevisaoDaRazao(aposConcessao));
            var documental = aposConcessao.RootElement.GetProperty("autorizacaoDocumental");
            Assert.AreEqual("vigente", documental.GetProperty("vigencia").GetString());
            Assert.AreEqual(primeiraAutorizacao, documental.GetProperty("id").GetGuid());
            Assert.IsTrue(documental.GetProperty("temJustificativa").GetBoolean());
        }

        using var substituicao = await Fase3Apoio.ConcederAutorizacaoAsync(
            administrador,
            pedidoId,
            "documental",
            "Nova conferência aceita pelo administrador.",
            versao);
        Assert.AreEqual(HttpStatusCode.Created, substituicao.StatusCode);
        using var substituicaoJson = await Fase2Apoio.LerJsonAsync(substituicao);
        var segundaAutorizacao = substituicaoJson.RootElement.GetProperty("autorizacaoId").GetGuid();

        using (var escopoSubstituicao = fabrica.Services.CreateScope())
        {
            var db = escopoSubstituicao.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
            Assert.AreEqual(2, await db.ConcessoesAutorizacao.CountAsync(concessao =>
                concessao.PedidoId == pedidoId && concessao.Tipo == "documental"));
        }

        using var revogacao = await Fase3Apoio.RevogarAutorizacaoAsync(
            administrador,
            pedidoId,
            segundaAutorizacao,
            "Divergência não aceita novamente.");
        Assert.AreEqual(HttpStatusCode.Created, revogacao.StatusCode, await revogacao.Content.ReadAsStringAsync());

        using var segundaRevogacao = await Fase3Apoio.RevogarAutorizacaoAsync(
            administrador,
            pedidoId,
            segundaAutorizacao,
            "Tentativa repetida.");
        Assert.AreEqual(HttpStatusCode.Conflict, segundaRevogacao.StatusCode);
        Assert.AreEqual("authorization_already_revoked", await Fase3Apoio.CodigoAsync(segundaRevogacao));

        using var financeira = await Fase3Apoio.ConcederAutorizacaoAsync(
            administrador,
            pedidoId,
            "financeira",
            "Saldo pendente aceito para a entrega futura.",
            versao);
        Assert.AreEqual(HttpStatusCode.Created, financeira.StatusCode, await financeira.Content.ReadAsStringAsync());
        using var financeiraJson = await Fase2Apoio.LerJsonAsync(financeira);
        Assert.AreEqual(1, financeiraJson.RootElement.GetProperty("revisaoReferencia").GetInt32());

        using var pagamento = await Fase3Apoio.PagarAsync(caixa, pedidoId, 60.00m, "pix", 1);
        Assert.AreEqual(HttpStatusCode.Created, pagamento.StatusCode, await pagamento.Content.ReadAsStringAsync());

        using var aposPagamento = await Fase3Apoio.CaixaAsync(caixa, pedidoId);
        Assert.AreEqual(
            "revogada",
            aposPagamento.RootElement.GetProperty("autorizacaoDocumental").GetProperty("vigencia").GetString());
        Assert.AreEqual(
            "obsoleta",
            aposPagamento.RootElement.GetProperty("autorizacaoFinanceira").GetProperty("vigencia").GetString());

        using var detalheAtual = await Fase3Apoio.DetalheAsync(caixa, pedidoId);
        var versaoAtual = Fase3Apoio.VersaoDoPedido(detalheAtual);
        using var busca = await Fase3Apoio.IniciarBuscaAsync(caixa, pedidoId, versaoAtual);
        Assert.AreEqual(HttpStatusCode.OK, busca.StatusCode, await busca.Content.ReadAsStringAsync());

        using var versaoDesatualizada = await Fase3Apoio.ConcederAutorizacaoAsync(
            administrador,
            pedidoId,
            "financeira",
            "Versão antiga.",
            versao);
        Assert.AreEqual(HttpStatusCode.Conflict, versaoDesatualizada.StatusCode);
        Assert.AreEqual("stale_withdrawal_version", await Fase3Apoio.CodigoAsync(versaoDesatualizada));
    }

    [TestMethod]
    public async Task JustificativaDeAutorizacaoEhProtegidaELidaSomentePeloAdministradorComAuditoria()
    {
        using var fabrica = Fase3Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);
        var (manobrista, _) = await Fase2Apoio.EntrarManobristaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa);
        var pedidoId = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(
            caixa,
            atendimentoId,
            conferencia: "divergente");

        using var detalhe = await Fase3Apoio.DetalheAsync(caixa, pedidoId);
        var versao = Fase3Apoio.VersaoDoPedido(detalhe);

        using var concessao = await Fase3Apoio.ConcederAutorizacaoAsync(
            administrador,
            pedidoId,
            "documental",
            "Justificativa documental sintética.",
            versao);
        Assert.AreEqual(HttpStatusCode.Created, concessao.StatusCode);
        using var concessaoJson = await Fase2Apoio.LerJsonAsync(concessao);
        var autorizacaoId = concessaoJson.RootElement.GetProperty("autorizacaoId").GetGuid();

        using var caixaNegado = await Fase3Apoio.LerJustificativaAutorizacaoAsync(
            caixa,
            pedidoId,
            autorizacaoId);
        Assert.AreEqual(HttpStatusCode.Forbidden, caixaNegado.StatusCode);

        using var manobristaNegado = await Fase3Apoio.LerJustificativaAutorizacaoAsync(
            manobrista,
            pedidoId,
            autorizacaoId);
        Assert.AreEqual(HttpStatusCode.Forbidden, manobristaNegado.StatusCode);

        using var leitura = await Fase3Apoio.LerJustificativaAutorizacaoAsync(
            administrador,
            pedidoId,
            autorizacaoId);
        Assert.AreEqual(HttpStatusCode.OK, leitura.StatusCode, await leitura.Content.ReadAsStringAsync());
        using var leituraJson = await Fase2Apoio.LerJsonAsync(leitura);
        Assert.AreEqual(
            "Justificativa documental sintética.",
            leituraJson.RootElement.GetProperty("conteudo").GetString());

        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
        Assert.IsTrue(await db.EventosAuditoriaNegocio.AnyAsync(evento =>
            evento.Tipo == "justificativa_lida" && evento.AlvoId == autorizacaoId));

        var concessaoArmazenada = await db.ConcessoesAutorizacao
            .AsNoTracking()
            .SingleAsync(registro => registro.Id == autorizacaoId);
        var envelopeAdulterado = concessaoArmazenada.JustificativaCifrada.ToArray();
        envelopeAdulterado[^1] ^= 0xFF;
        SqlTeste.Executar(
            fabrica.NomeBanco,
            $"UPDATE [ConcessoesAutorizacao] SET [JustificativaCifrada] = 0x{Convert.ToHexString(envelopeAdulterado)} WHERE [Id] = '{autorizacaoId}';");

        using var adulterada = await Fase3Apoio.LerJustificativaAutorizacaoAsync(
            administrador,
            pedidoId,
            autorizacaoId);
        Assert.AreEqual(HttpStatusCode.InternalServerError, adulterada.StatusCode);
    }

    [TestMethod]
    public async Task AutorizacaoNaoAlteraSaldoEstadoNemConsomeEntrega()
    {
        using var fabrica = Fase3Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa, tarifa: 60.00m);
        var pedidoId = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(
            caixa,
            atendimentoId,
            conferencia: "divergente");

        using var detalhe = await Fase3Apoio.DetalheAsync(caixa, pedidoId);
        var versao = Fase3Apoio.VersaoDoPedido(detalhe);

        using var concessao = await Fase3Apoio.ConcederAutorizacaoAsync(
            administrador,
            pedidoId,
            "documental",
            "Justificativa sintética.",
            versao);
        Assert.AreEqual(HttpStatusCode.Created, concessao.StatusCode);

        using var caixaApos = await Fase3Apoio.CaixaAsync(caixa, pedidoId);
        Assert.AreEqual(60.00m, Fase3Apoio.Saldo(caixaApos));
        Assert.AreEqual(1, Fase3Apoio.RevisaoDaRazao(caixaApos));

        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
        var atendimento = await db.Atendimentos.AsNoTracking()
            .SingleAsync(registro => registro.Id == atendimentoId);
        Assert.AreEqual("aguardando", atendimento.Estado);
        Assert.AreEqual(1, await db.Ocupacoes.CountAsync(ocupacao =>
            ocupacao.AtendimentoId == atendimentoId && ocupacao.FimUtc == null));

        using var consumo = await administrador.PostAsync(
            $"/api/v1/withdrawals/{pedidoId}/authorizations/{Guid.NewGuid()}/consume",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        Assert.IsTrue(consumo.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);

        using var detalheFinal = await Fase3Apoio.DetalheAsync(caixa, pedidoId);
        Assert.AreEqual("aguardando", detalheFinal.RootElement.GetProperty("estado").GetString());
    }
}
