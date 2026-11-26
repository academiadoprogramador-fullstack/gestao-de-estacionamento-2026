using System.Net;
using System.Text.Json;
using Estacionamento.Application.Fotos;
using Estacionamento.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Estacionamento.IntegrationTests;

[TestClass]
public sealed class RetencaoEManutencaoFotosTestes
{
    [TestMethod]
    public async Task FotoVencidaDeixaDeSerLidaEsDescartadaPelaManutencao()
    {
        using var fabrica = Fase5Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);
        var (manobrista, _) = await Fase2Apoio.EntrarManobristaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa);
        var pedidoId = await Fase3Apoio.RegistrarPedidoConfirmadoAsync(caixa, atendimentoId);
        await Fase4Apoio.RegistrarVistoriaConfirmadaAsync(manobrista, atendimentoId);

        using var foto = await Fase5Apoio.EnviarFotoConfirmadaAsync(
            manobrista,
            atendimentoId,
            "saida",
            await Fase4Apoio.VersaoAtendimentoAsync(manobrista, atendimentoId));
        var fotoId = foto.RootElement.GetProperty("fotoId").GetGuid();

        using (var caixaAtual = await Fase3Apoio.CaixaAsync(caixa, pedidoId))
        {
            await Fase3Apoio.PagarAsync(
                caixa,
                pedidoId,
                Fase3Apoio.Saldo(caixaAtual),
                "dinheiro",
                Fase3Apoio.RevisaoDaRazao(caixaAtual));
        }

        await Fase4Apoio.ConfirmarEntregaConfirmadaAsync(caixa, pedidoId);

        SqlTeste.Executar(
            fabrica.NomeBanco,
            "UPDATE [EntregasVeiculo] SET [EntregueEmUtc] = DATEADD(day, -100, SYSDATETIMEOFFSET()); " +
            "UPDATE [LancamentosFinanceiros] SET [OcorridoEmUtc] = DATEADD(day, -100, SYSDATETIMEOFFSET());");

        using var listaVencida = await Fase5Apoio.ListarFotosAsync(manobrista, atendimentoId, "saida");
        Assert.IsTrue(listaVencida.RootElement.GetProperty("vencido").GetBoolean());
        Assert.AreEqual(0, listaVencida.RootElement.GetProperty("quantidade").GetInt32());

        using var conteudo = await Fase5Apoio.LerConteudoAsync(manobrista, atendimentoId, "saida", fotoId);
        Assert.AreEqual(HttpStatusCode.NotFound, conteudo.StatusCode);
        Assert.AreEqual("photo_expired", await Fase4Apoio.CodigoAsync(conteudo));

        using (var escopo = fabrica.Services.CreateScope())
        {
            var manutencao = escopo.ServiceProvider.GetRequiredService<IServicoManutencaoFotos>();
            await manutencao.ExecutarLoteAsync();
            await manutencao.ExecutarLoteAsync();
        }

        using (var escopo = fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
            var metadados = await db.FotosVistoria.AsNoTracking().SingleAsync(registro => registro.Id == fotoId);
            Assert.AreEqual("descartada", metadados.Estado);
            Assert.AreEqual(0, fabrica.ArmazenamentoFotos.Objetos.Count);
            Assert.IsTrue(await db.EventosAtendimento.AnyAsync(evento =>
                evento.AtendimentoId == atendimentoId &&
                evento.Tipo == "foto_descartada"));
            Assert.IsFalse(await db.TarefasFotos.AnyAsync(tarefa =>
                tarefa.Estado == "pendente"));
        }
    }

    [TestMethod]
    public async Task AtendimentoAtivoOuComSaldoNaoEntraNaRetencao()
    {
        using var fabrica = Fase5Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa);
        using var foto = await Fase5Apoio.EnviarFotoConfirmadaAsync(
            caixa,
            atendimentoId,
            "entrada");

        SqlTeste.Executar(
            fabrica.NomeBanco,
            "UPDATE [FotosVistoria] SET [ConfirmadoEmUtc] = DATEADD(day, -100, SYSDATETIMEOFFSET());");

        using (var escopo = fabrica.Services.CreateScope())
        {
            var manutencao = escopo.ServiceProvider.GetRequiredService<IServicoManutencaoFotos>();
            await manutencao.ExecutarLoteAsync();
        }

        using (var escopo = fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
            var metadados = await db.FotosVistoria.AsNoTracking().SingleAsync();
            Assert.AreEqual("disponivel", metadados.Estado);
            Assert.AreEqual(1, fabrica.ArmazenamentoFotos.Objetos.Count);
        }
    }

    [TestMethod]
    public async Task FalhaAntesDaConfirmacaoGeraLimpezaRetomavelSemExporFoto()
    {
        using var fabrica = Fase5Apoio.NovaFabrica();
        await fabrica.ProvisionarContasAsync();
        var (administrador, _) = await Fase2Apoio.EntrarAdministradorAsync(fabrica);
        var (caixa, _) = await Fase2Apoio.EntrarCaixaAsync(fabrica);

        var atendimentoId = await Fase3Apoio.PrepararAtendimentoAsync(administrador, caixa);
        var versao = await Fase4Apoio.VersaoAtendimentoAsync(caixa, atendimentoId);
        fabrica.ArmazenamentoFotos.FalharProximoEnvio = true;

        using var falha = await Fase5Apoio.EnviarFotoAsync(
            caixa,
            atendimentoId,
            "entrada",
            Fase5Apoio.GerarImagem(),
            versaoAtendimento: versao);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, falha.StatusCode);

        using (var escopo = fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
            Assert.IsTrue(await db.TarefasFotos.AnyAsync(tarefa => tarefa.Estado == "pendente"));
        }

        SqlTeste.Executar(
            fabrica.NomeBanco,
            "UPDATE [TarefasFotos] SET [LeaseAteUtc] = DATEADD(minute, 5, SYSDATETIMEOFFSET());");

        using (var escopo = fabrica.Services.CreateScope())
        {
            var manutencao = escopo.ServiceProvider.GetRequiredService<IServicoManutencaoFotos>();
            await manutencao.ExecutarLoteAsync();
        }

        using (var escopo = fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
            var pendente = await db.TarefasFotos.AsNoTracking().SingleAsync();
            Assert.AreEqual("pendente", pendente.Estado);
        }

        SqlTeste.Executar(
            fabrica.NomeBanco,
            "UPDATE [TarefasFotos] SET [LeaseAteUtc] = NULL;");

        fabrica.ArmazenamentoFotos.FalharProximaExclusao = true;

        using (var escopo = fabrica.Services.CreateScope())
        {
            var manutencao = escopo.ServiceProvider.GetRequiredService<IServicoManutencaoFotos>();
            await manutencao.ExecutarLoteAsync();
        }

        using (var escopo = fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
            var tarefa = await db.TarefasFotos.AsNoTracking().SingleAsync();
            Assert.AreEqual("pendente", tarefa.Estado);
        }

        using (var escopo = fabrica.Services.CreateScope())
        {
            var manutencao = escopo.ServiceProvider.GetRequiredService<IServicoManutencaoFotos>();
            await manutencao.ExecutarLoteAsync();
        }

        using (var escopo = fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<EstacionamentoDbContext>();
            var metadados = await db.FotosVistoria.AsNoTracking().SingleAsync();
            Assert.AreEqual("descartada", metadados.Estado);
            Assert.AreEqual(0, fabrica.ArmazenamentoFotos.Objetos.Count);
        }
    }
}
